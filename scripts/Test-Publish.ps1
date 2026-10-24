param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$ProbeDirectory,
    [string]$OutputDirectory,
    [switch]$StartupOnly,
    [string]$ResumeRoot
)

if (-not $ProbeDirectory) { $ProbeDirectory = Join-Path $PSScriptRoot '../NovaLauncher.LaunchProbe/bin/Release/net9.0-windows' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '../artifacts/verification/publish' }

# Run with Windows PowerShell -STA on an interactive desktop. This drives the actual
# copied publish EXE; it never constructs a product ViewModel in the test process.
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Use powershell.exe -STA -File scripts/Test-Publish.ps1 ...' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing @'
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public static class PublishNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr w, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr state);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wparam, string text);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    public static bool SetFileName(IntPtr container, string path) {
        IntPtr edit = IntPtr.Zero;
        var name = new System.Text.StringBuilder(128);
        GetClassName(container, name, 128);
        if (name.ToString() == "Edit") edit = container;
        EnumChildWindows(container, (h,s) => { name.Clear(); GetClassName(h,name,128); if(name.ToString()=="Edit") {edit=h; return false;} return true; }, IntPtr.Zero);
        if (edit == IntPtr.Zero) return false;
        return SendMessage(edit, 0x000C, IntPtr.Zero, path) != IntPtr.Zero;
    }

}
'@
[PublishNative]::SetProcessDPIAware() | Out-Null
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NovaLauncher-Publish-' + [Guid]::NewGuid().ToString('N'))
if ($ResumeRoot) {
    $testRoot = [IO.Path]::GetFullPath($ResumeRoot)
    if ([IO.Path]::GetDirectoryName($testRoot) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($testRoot) -notlike 'NovaLauncher-Publish-*' -or -not (Test-Path (Join-Path $testRoot 'data/.nova-verification'))) { throw 'Invalid resume fixture' }
}
$portable = Join-Path $testRoot 'portable'
$dataRoot = Join-Path $testRoot 'data'
$probeOutput = Join-Path $testRoot 'probe-output'
New-Item -ItemType Directory -Path $portable, $dataRoot, $probeOutput -Force | Out-Null
Set-Content -LiteralPath (Join-Path $dataRoot '.nova-verification') -Value 'Isolated publish verification fixture' -Encoding UTF8
Copy-Item -Path (Join-Path $PublishDirectory '*') -Destination $portable -Recurse -Force
$exe = Join-Path $portable 'NovaLauncher.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing published EXE: $exe" }
$checks = [Collections.Generic.List[string]]::new()
$appProcess = $null
$completed = $false
$originalCursor = [Windows.Forms.Cursor]::Position
$oldProbeOutput = $env:NOVA_LAUNCH_TEST_OUTPUT
$env:NOVA_LAUNCH_TEST_OUTPUT = $probeOutput
$defaultRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NovaLauncher'
function Get-DefaultFingerprint {
    if (-not (Test-Path -LiteralPath $defaultRoot)) { return 'absent' }
    return ((Get-ChildItem -LiteralPath $defaultRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        '{0}|{1}|{2}' -f $_.FullName, $_.Length, $_.LastWriteTimeUtc.Ticks
    }) -join "`n")
}
$defaultBefore = Get-DefaultFingerprint
function Pass([string]$message) { $checks.Add($message); Write-Output "PASS: $message" }
function Wait-For([scriptblock]$condition, [string]$description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do { if (& $condition) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timeout: $description"
}
function Find-Ui([string]$value, [switch]$Name, [switch]$Optional, [switch]$Invokable, [switch]$Editable, [switch]$Selectable) {
    if (-not $Optional) {
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            $ready = Find-Ui $value -Name:$Name -Invokable:$Invokable -Editable:$Editable -Selectable:$Selectable -Optional
            if ($null -ne $ready) { return $ready }
            Start-Sleep -Milliseconds 100
        }
        throw "Missing UI after render wait: $value"
    }
    $property = [Windows.Automation.AutomationElement]::AutomationIdProperty
    if ($Name) { $property = [Windows.Automation.AutomationElement]::NameProperty }
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$appProcess.Id),
        [Windows.Automation.PropertyCondition]::new($property, $value))
    if ($Invokable) { $condition = [Windows.Automation.AndCondition]::new($condition, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsInvokePatternAvailableProperty, $true)) }
    if ($Editable) { $condition = [Windows.Automation.AndCondition]::new($condition, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsValuePatternAvailableProperty, $true)) }
    if ($Selectable) { $condition = [Windows.Automation.AndCondition]::new($condition, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true)) }
    # Popups/file dialogs are separate HWNDs, so search the desktop with a strict PID filter.
    $found = $null
    $tops = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$appProcess.Id))
    foreach ($top in $tops) { $found = $top.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition); if ($null -ne $found) { break } }
    if ($null -eq $found -and $value -eq 'LaunchApplication' -and -not $Invokable) {
        $grid = Find-Ui 'ApplicationGrid' -Optional
        if ($null -ne $grid) {
            $items = $grid.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::DataItem))
            if ($items.Count -eq 1) { $found = $items[0] }
        }
    }
    if ($null -eq $found -and -not $Optional) { throw "Missing UI: $value" }
    return $found
}
function Invoke-Ui([string]$value, [switch]$Name) {
    $element = Find-Ui $value -Name:$Name -Invokable
    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
}
function Set-Text([string]$value, [string]$text, [switch]$Name) {
    (Find-Ui $value -Name:$Name -Editable).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    Start-Sleep -Milliseconds 150
}
function Send-Keys([string]$keys) {
    [uint32]$foregroundPid = 0
    [PublishNative]::GetWindowThreadProcessId([PublishNative]::GetForegroundWindow(), [ref]$foregroundPid) | Out-Null
    if ($foregroundPid -ne $appProcess.Id) { throw 'Refusing keyboard input: foreground belongs to another process' }
    [Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 250
}
function Start-Nova {
    $script:appProcess = Start-Process -FilePath $exe -ArgumentList ('--verification-data-root="' + $dataRoot + '"') -WorkingDirectory $portable -PassThru
    Wait-For { $appProcess.Refresh(); if ($appProcess.HasExited) { throw "Published EXE exited: $($appProcess.ExitCode)" }; $appProcess.MainWindowHandle -ne [IntPtr]::Zero } 'published window'
    $script:window = [Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
    [PublishNative]::SetWindowPos($appProcess.MainWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 3) | Out-Null
    [PublishNative]::SetForegroundWindow($appProcess.MainWindowHandle) | Out-Null
    Wait-For { Test-Path -LiteralPath (Join-Path $dataRoot 'Data/nova.db') } 'isolated SQLite creation'
    Start-Sleep -Milliseconds 600
}
function Close-Nova {
    # 8.6 defaults X to residency. This legacy persistence smoke explicitly opts out;
    # real tray Exit is verified separately by Test-TrayStartup.ps1.
    Open-Settings
    $residency = Find-Ui 'CloseToTray' -Optional
    if ($null -ne $residency) {
        $toggle = $residency.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
        if ($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) {
            $toggle.Toggle()
            Wait-For { $toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::Off } 'close-to-tray disabled for persistence smoke'
        }
    }
    Invoke-Ui 'CloseButton'
    if (-not $appProcess.WaitForExit(15000)) { throw 'Published EXE did not shut down' }
    if ($appProcess.ExitCode -ne 0) { throw "Published EXE exit code $($appProcess.ExitCode)" }
    $appProcess.Dispose(); $script:appProcess = $null
}
function Save-Window([string]$name) {
    $rect = New-Object PublishNative+RECT
    [PublishNative]::GetWindowRect($appProcess.MainWindowHandle, [ref]$rect) | Out-Null
    $bitmap = [Drawing.Bitmap]::new($rect.Right-$rect.Left, $rect.Bottom-$rect.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc = $graphics.GetHdc()
        try { if (-not [PublishNative]::PrintWindow($appProcess.MainWindowHandle, $dc, 2)) { throw 'PrintWindow failed' } }
        finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save((Join-Path $OutputDirectory $name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Select-Nav([string]$name) {
    (Find-Ui $name -Name).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
}
function Assert-Recent {
    $grid = Find-Ui 'ApplicationGrid'
    $items = $grid.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::DataItem))
    if ($items.Count -ne 1) { throw 'Recent did not render the single launched fixture' }
    Save-Window 'recent.png'
}
function Open-CardMenu {
    $card = Find-Ui 'LaunchApplication'
    $rect = $card.Current.BoundingRectangle
    $point = [Drawing.Point]::new([int]($rect.Left+$rect.Width/2), [int]($rect.Top+$rect.Height/2))
    [uint32]$targetPid = 0
    [PublishNative]::GetWindowThreadProcessId([PublishNative]::WindowFromPoint([Drawing.Point]::new([int]$point.X,[int]$point.Y)),[ref]$targetPid) | Out-Null
    if ($targetPid -ne $appProcess.Id) { throw 'Card click target is obscured' }
    [PublishNative]::SetCursorPos([int]$point.X,[int]$point.Y) | Out-Null
    [PublishNative]::mouse_event(8,0,0,0,[UIntPtr]::Zero)
    [PublishNative]::mouse_event(16,0,0,0,[UIntPtr]::Zero)
    Wait-For { $null -ne (Find-Ui 'FavoriteMenuItem' -Optional) } 'card context menu'
}
function Open-Settings {
    # Avatar is a toggle. The persistence check leaves the flyout open, so clicking
    # it unconditionally would close the very popup we are about to search.
    if ($null -eq (Find-Ui 'ProfileSettings' -Invokable -Optional)) {
        Invoke-Ui 'ProfileAvatar'
    }
    Invoke-Ui 'ProfileSettings'
}
function Choose-File([string]$path) {
    # Windows common file dialog supports Alt+N for its filename edit across tested locales.
    Wait-For { $null -ne (Find-Ui '1148' -Optional) } 'native file picker'
    $filename = Find-Ui '1148'
    if (-not [PublishNative]::SetFileName([IntPtr]$filename.Current.NativeWindowHandle, $path)) { throw 'Native filename text was not set' }
    $open = Find-Ui '打开(O)' -Name
    [PublishNative]::PostMessage([IntPtr]$open.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Wait-For { $null -eq (Find-Ui '1148' -Optional) } 'native picker closes'
    Start-Sleep -Milliseconds 600
}
try {
    $forbidden = Get-ChildItem -LiteralPath $portable -Recurse -File | Where-Object { $_.Extension -in '.pdb','.db','.log' -or $_.Name -match '\.db-(wal|shm)$' }
    if ($forbidden) { throw "Publish contains prohibited files: $($forbidden.Name -join ', ')" }
    Pass 'Copied portable directory has no database/log/PDB payload'
    Start-Nova
    Pass $(if ($ResumeRoot) { 'Real copied publish EXE starts with prepared isolated data' } else { 'Real copied publish EXE starts with isolated fresh SQLite data' })
    Save-Window 'fresh.png'
    $manifest = Get-Content (Join-Path $portable 'release-manifest.json') -Raw | ConvertFrom-Json
    $runtime = @($appProcess.Modules | Where-Object ModuleName -eq 'coreclr.dll')[0].FileName
    if ($manifest.selfContained -and -not $manifest.singleFile -and -not $runtime.StartsWith($portable + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected shared runtime: $runtime" }
    if ($runtime) { Pass "Loaded runtime: $runtime" } elseif ($manifest.singleFile) { Write-Output 'INFO: Single-file startup succeeded; standalone coreclr module path not exposed' } else { throw 'Expected coreclr module missing' }
    if (-not $ResumeRoot) {
    [PublishNative]::ShowWindow($appProcess.MainWindowHandle, 6) | Out-Null
    $secondary = Start-Process -FilePath $exe -ArgumentList ('--verification-data-root="' + $dataRoot + '"') -WorkingDirectory $portable -PassThru
    try {
        if (-not $secondary.WaitForExit(10000) -or $secondary.ExitCode -ne 0) { throw 'Secondary launch did not exit successfully' }
        Wait-For { -not [PublishNative]::IsIconic($appProcess.MainWindowHandle) } 'primary restored'
        Wait-For { [PublishNative]::GetForegroundWindow() -eq $appProcess.MainWindowHandle } 'primary foreground activation'
        Pass 'Secondary exits, restores minimized primary and activates original window'
    } finally { if (-not $secondary.HasExited) { $secondary.Kill() }; $secondary.Dispose() }
    $appProcess.Kill(); $appProcess.WaitForExit(10000) | Out-Null; $appProcess.Dispose(); $appProcess = $null
    Start-Nova
    Pass 'Forced process termination releases ownership; published EXE restarts'
    }
    if (-not $StartupOnly) {
        if (-not $ResumeRoot) {
        $probeTarget = Join-Path $testRoot 'probe'
        New-Item -ItemType Directory -Path $probeTarget | Out-Null
        Copy-Item -Path (Join-Path $ProbeDirectory '*') -Destination $probeTarget -Recurse
        $probeExe = Join-Path $probeTarget 'NovaLauncher.LaunchProbe.exe'
        if (-not (Test-Path -LiteralPath $probeExe)) { throw 'Build LaunchProbe first' }
        Write-Output 'STEP: locating workspace for OLE drop'
        $bounds = (Find-Ui 'LauncherWorkspace').Current.BoundingRectangle
        Write-Output "STEP: OLE drop to $bounds"
        $dropHelper = Join-Path $PSScriptRoot '../NovaLauncher.Verification/bin/Release/net9.0-windows/NovaLauncher.Verification.exe'
        $dropArgs = @('--external-drop', ('"' + $probeExe + '"'), [int]($bounds.Left+$bounds.Width/2), [int]($bounds.Top+$bounds.Height/2), $appProcess.Id)
        $drop = Start-Process -FilePath $dropHelper -ArgumentList $dropArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'drop-helper.log')
        $null = $drop.Handle
        try {
            if (-not $drop.WaitForExit(15000)) { throw 'OLE helper timeout (interactive desktop unavailable)' }
            if ($drop.ExitCode -ne 0) { throw "OLE helper failed: exit=$($drop.ExitCode); see drop-helper.log" }
        } finally { if (-not $drop.HasExited) { $drop.Kill() }; $drop.Dispose() }
        Wait-For { $null -ne (Find-Ui 'LaunchApplication' -Optional) } 'OLE import'
        [PublishNative]::SetForegroundWindow($appProcess.MainWindowHandle) | Out-Null
        Pass 'Real OLE EXE import reaches published process'
        Invoke-Ui 'LaunchApplication'
        Wait-For { @(Get-ChildItem -LiteralPath $probeOutput -Filter '*.json').Count -gt 0 } 'actual LaunchProbe process output'
        Pass 'Published application launches real isolated probe'
        Open-CardMenu
        Invoke-Ui 'FavoriteMenuItem'
        Open-CardMenu
        (Find-Ui '移动到分类' -Name).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 200
        # Select the submenu item by ControlType, avoiding the Sidebar item with the same name.
        $menuCondition = [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$appProcess.Id),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::MenuItem),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, '游戏'))
        $menu = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $menuCondition)
        if ($null -eq $menu) { throw 'Category submenu not found' }
        $menu.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 300
        Select-Nav '收藏'; Find-Ui 'LaunchApplication' | Out-Null
        Select-Nav '最近使用'; Assert-Recent
        Select-Nav '游戏'; Find-Ui 'LaunchApplication' | Out-Null
        Pass 'Category, Favorites and Recent project imported launched application'
        } else {
            Select-Nav '收藏'; Find-Ui 'LaunchApplication' | Out-Null
            Select-Nav '最近使用'; Assert-Recent
            Select-Nav '游戏'; Find-Ui 'LaunchApplication' | Out-Null
            Pass 'Prepared fixture renders app, launch, favorite and category state'
        }
        Open-CardMenu; Invoke-Ui '重命名' -Name
        Set-Text '应用名称' '微信发布测试' -Name
        Invoke-Ui '保存' -Name
        Send-Keys '^k'
        Set-Text 'QuickQueryBox' 'weixin'
        Wait-For { $null -ne (Find-Ui '微信发布测试' -Name -Optional) } 'pinyin result'
        Save-Window 'pinyin.png'
        Send-Keys '{ESC}'
        Pass 'Context-menu rename and Ctrl+K full-pinyin search'
        $image = Join-Path $testRoot 'fixture.png'
        $bitmap = [Drawing.Bitmap]::new(320, 200)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.Clear([Drawing.Color]::SteelBlue); $bitmap.Save($image, [Drawing.Imaging.ImageFormat]::Png) }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
        Open-CardMenu; Invoke-Ui '更换图标' -Name; Choose-File $image
        Invoke-Ui 'ProfileAvatar'; Invoke-Ui 'EditProfileName'
        Set-Text '用户名称' '发布验收' -Name; Invoke-Ui '保存' -Name
        Invoke-Ui '更换头像' -Name; Choose-File $image
        Open-Settings
        (Find-Ui '背景设置' -Name).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $mode = Find-Ui 'ModeSelector'
        $mode.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $choice = Find-Ui '自定义图片' -Name
        $item = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($choice)
        $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Invoke-Ui '选择背景图片' -Name; Choose-File $image
        Save-Window 'settings-background.png'
        Pass 'Custom icon, profile name/avatar and custom background through native UI'
        Close-Nova
        $assetsBefore = @(Get-ChildItem -LiteralPath $dataRoot -Recurse -Filter '*.png' | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
        if ($assetsBefore.Count -lt 3) { throw 'Expected avatar, background and custom icon persisted files' }
        Remove-Item -LiteralPath $image
        Start-Nova
        Select-Nav '收藏'; Find-Ui 'LaunchApplication' | Out-Null
        Select-Nav '最近使用'; Assert-Recent
        Select-Nav '游戏'; Find-Ui 'LaunchApplication' | Out-Null
        Send-Keys '^k'; Set-Text 'QuickQueryBox' 'wxfbcs'
        Wait-For { $null -ne (Find-Ui '微信发布测试' -Name -Optional) } 'restored initials search result'
        Send-Keys '{ESC}'
        Invoke-Ui 'ProfileAvatar'; Find-Ui '发布验收' -Name | Out-Null
        Find-Ui '恢复默认头像' -Name | Out-Null
        Pass 'Restart restores application projections, name and pinyin index'
        Save-Window 'restarted.png'
    }
    Close-Nova
    if ((Get-DefaultFingerprint) -cne $defaultBefore) { throw 'Default user directory changed during test; investigate external activity or isolation failure' }
    Pass 'Default user directory file metadata unchanged'
    [ordered]@{ Checks=$checks.Count; Passed=$checks.ToArray(); Source=$PublishDirectory; CopiedExecutable=$exe; DataRoot=$dataRoot; RealCleanMachine=$false; Mode=$(if($StartupOnly){'StartupOnly'}elseif($ResumeRoot){'ContinuedSmoke'}else{'FullSmoke'}) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json') -Encoding UTF8
    $completed = $true
} catch {
    Write-Output $_.ScriptStackTrace
    if ($null -ne $appProcess -and -not $appProcess.HasExited) {
        $tree = [Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
        $tree.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { '{0}|{1}|{2}|pid={3}' -f $_.Current.AutomationId,$_.Current.Name,$_.Current.ControlType.ProgrammaticName,$_.Current.ProcessId } | Set-Content (Join-Path $OutputDirectory 'ui-tree.txt') -Encoding UTF8
    }
    if ($null -ne $appProcess -and -not $appProcess.HasExited) { try { Save-Window 'failure.png' } catch { } }
    throw
} finally {
    if ($null -ne $appProcess) { if (-not $appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit(10000) | Out-Null }; $appProcess.Dispose() }
    $env:NOVA_LAUNCH_TEST_OUTPUT = $oldProbeOutput
    [PublishNative]::SetCursorPos($originalCursor.X, $originalCursor.Y) | Out-Null
    # Keep fixture on failure for diagnosis; successful runs delete only this checked generated temp root.
    if ($completed) {
        $resolved = [IO.Path]::GetFullPath($testRoot)
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'NovaLauncher-Publish-*') { throw 'Refusing unsafe fixture cleanup' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } else { Write-Warning "Failed fixture retained: $testRoot" }
}
