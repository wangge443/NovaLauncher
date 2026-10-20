# Historical UI Shell check: navigation expectations predate 7.x; not the 8.1 release smoke entry.
param(
    [string]$ScreenshotDirectory = (Join-Path $PSScriptRoot '../docs/screenshots'),
    [switch]$DesktopCapture,
    [switch]$ForceFallback
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShellNative {
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@
[ShellNative]::SetProcessDPIAware() | Out-Null
$exe = Join-Path $PSScriptRoot '../NovaLauncher.App/bin/Release/net9.0-windows/NovaLauncher.exe'
$shell = $null
$appProcess = $null
$originalCursor = [System.Windows.Forms.Cursor]::Position

function Find-Element([string]$id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $element) { throw "Missing UI element: $id" }
    return $element
}

function Invoke-Button([string]$id) {
    $button = Find-Element $id
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 250
}

function Save-Window([string]$name) {
    $name = $name.Replace('3.1', '3.2')
    if ($ForceFallback) { $name = $name.Replace('.png', '-fallback.png') }
    $rect = New-Object ShellNative+RECT
    [ShellNative]::GetWindowRect($appProcess.MainWindowHandle, [ref]$rect) | Out-Null
    $bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        if ($DesktopCapture) {
            $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        } else {
            $dc = $graphics.GetHdc()
            try {
                if (-not [ShellNative]::PrintWindow($appProcess.MainWindowHandle, $dc, 2)) { throw 'Window capture failed' }
            } finally { $graphics.ReleaseHdc($dc) }
        }
        $bitmap.Save((Join-Path $ScreenshotDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Assert-Backdrop {
    Start-Sleep -Milliseconds 500
    $status = (Find-Element 'ShellBackground').Current.ItemStatus
    $material = 0
    $result = [ShellNative]::DwmGetWindowAttribute($appProcess.MainWindowHandle, 38, [ref]$material, 4)
    if ($ForceFallback) {
        if ($status -ne 'Fallback:DiagnosticOverride' -or $material -eq 3) { throw "Fallback failed: $status / $material" }
    } elseif ($DesktopCapture) {
        if ($result -lt 0 -or $material -ne 3 -or $status -ne 'DesktopAcrylic') { throw "Acrylic unavailable: $status / $material / $result" }
    }
    Write-Output "PASS: Backdrop status=$status; DWM material=$material."
}

function Save-DesktopContext {
    $rect = New-Object ShellNative+RECT
    [ShellNative]::GetWindowRect($appProcess.MainWindowHandle, [ref]$rect) | Out-Null
    $screen = [System.Windows.Forms.Screen]::FromHandle($appProcess.MainWindowHandle).Bounds
    $left = [Math]::Max($screen.Left, $rect.Left - 120)
    $top = [Math]::Max($screen.Top, $rect.Top - 100)
    $width = [Math]::Min($screen.Right, $rect.Right + 120) - $left
    $height = [Math]::Min($screen.Bottom, $rect.Bottom + 100) - $top
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $name = 'launcher-desktop-3.2.png'
    if ($ForceFallback) { $name = 'launcher-desktop-3.2-fallback.png' }
    try {
        $graphics.CopyFromScreen($left, $top, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $ScreenshotDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Drag-Splitter([int]$distance) {
    $rect = (Find-Element 'SidebarSplitter').Current.BoundingRectangle
    $startX = [int]($rect.X + $rect.Width / 2)
    $startY = [int]($rect.Y + $rect.Height / 2)
    [ShellNative]::SetCursorPos($startX, $startY) | Out-Null
    [ShellNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    try {
        for ($step = 1; $step -le 10; $step++) {
            [ShellNative]::SetCursorPos(($startX + [int]($distance * $step / 10)), $startY) | Out-Null
            Start-Sleep -Milliseconds 20
        }
    } finally { [ShellNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 200
}

function Assert-SidebarLimits {
    $width = (Find-Element 'Sidebar').Current.BoundingRectangle.Width
    $windowWidth = $window.Current.BoundingRectangle.Width
    if ($width -lt (210 * $dpiScale - 2)) { throw 'Sidebar is narrower than 210 DIP' }
    if ($width -gt ($windowWidth * 0.35 + 2)) { throw 'Sidebar exceeds 35% of window width' }
}

try {
    if ($DesktopCapture) {
        $shell = New-Object -ComObject Shell.Application
        $shell.MinimizeAll()
        Start-Sleep -Milliseconds 700
    }
    $previousOverride = $env:NOVA_DISABLE_SYSTEM_BACKDROP
    try {
        if ($ForceFallback) { $env:NOVA_DISABLE_SYSTEM_BACKDROP = '1' }
        else { Remove-Item Env:NOVA_DISABLE_SYSTEM_BACKDROP -ErrorAction SilentlyContinue }
        $appProcess = Start-Process -FilePath $exe -PassThru
    } finally { $env:NOVA_DISABLE_SYSTEM_BACKDROP = $previousOverride }
    if (-not $appProcess.WaitForInputIdle(15000)) { throw 'Application did not become idle' }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $appProcess.Refresh()
        if ($appProcess.HasExited) { throw "Application exited: $($appProcess.ExitCode)" }
        if ($appProcess.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($appProcess.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Main window was not created' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
    $windowPattern = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    [ShellNative]::SetForegroundWindow($appProcess.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300
    if ($window.Current.Name -ne 'Nova Launcher') { throw 'Unexpected title' }
    Assert-Backdrop
    $dpiScale = [ShellNative]::GetDpiForWindow($appProcess.MainWindowHandle) / 96.0
    if ((Find-Element 'DropHint').Current.Name -ne '请将文件拖拽至此') { throw 'Empty workspace prompt missing' }
    $workspace = Find-Element 'LauncherWorkspace'
    $textNodes = $workspace.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text))
    $workspaceText = @($textNodes | ForEach-Object { $_.Current.Name })
    if ($workspaceText.Count -ne 2 -or $workspaceText -notcontains '支持 .exe 和 .lnk') { throw 'Unexpected workspace copy' }

    $navigation = Find-Element 'SidebarNavigation'
    $items = $navigation.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem))
    if ($items.Count -ne 6) { throw "Expected six navigation items, found $($items.Count)" }
    foreach ($item in $items) {
        $selection = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        Start-Sleep -Milliseconds 100
        if (-not $selection.Current.IsSelected) { throw 'Navigation selection failed' }
    }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-Output 'PASS: Minimal workspace copy and six navigation selections.'

    $items[0].SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    Start-Sleep -Milliseconds 150
    if (-not $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Keyboard navigation failed' }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-Output 'PASS: Keyboard down-arrow navigation.'

    $bounds = $window.Current.BoundingRectangle
    $captionPoint = [IntPtr](([int]($bounds.Y + 20) -shl 16) -bor [int]($bounds.X + 300))
    $hit = [ShellNative]::SendMessage($appProcess.MainWindowHandle, 0x84, [IntPtr]::Zero, $captionPoint)
    if ($hit.ToInt32() -ne 2) { throw 'Title bar is not a native draggable caption' }
    Write-Output 'PASS: Native title-bar drag hit target.'
    [ShellNative]::SendMessage($appProcess.MainWindowHandle, 0xA3, [IntPtr]2, $captionPoint) | Out-Null
    Start-Sleep -Milliseconds 250
    if ($windowPattern.Current.WindowVisualState -ne 'Maximized') { throw 'Caption double-click maximize failed' }
    [ShellNative]::SendMessage($appProcess.MainWindowHandle, 0xA3, [IntPtr]2, $captionPoint) | Out-Null
    Start-Sleep -Milliseconds 250
    if ($windowPattern.Current.WindowVisualState -ne 'Normal') { throw 'Caption double-click restore failed' }
    Write-Output 'PASS: Caption double-click maximize and restore.'

    # Exercise the actual caption buttons, not only WindowPattern state changes.
    Invoke-Button 'MaximizeButton'
    if ($windowPattern.Current.WindowVisualState -ne 'Maximized') { throw 'Maximize failed' }
    if ((Find-Element 'MaximizeButton').Current.Name -ne '还原') { throw 'Restore label failed' }
    Assert-Backdrop
    $workArea = [System.Windows.Forms.Screen]::FromHandle($appProcess.MainWindowHandle).WorkingArea
    $closeBounds = (Find-Element 'CloseButton').Current.BoundingRectangle
    if ($closeBounds.Top -lt $workArea.Top -or $closeBounds.Right -gt $workArea.Right + 1) { throw 'Maximized caption outside work area' }
    Invoke-Button 'MaximizeButton'
    if ($windowPattern.Current.WindowVisualState -ne 'Normal') { throw 'Restore failed' }
    Invoke-Button 'MinimizeButton'
    if ($windowPattern.Current.WindowVisualState -ne 'Minimized') { throw 'Minimize failed' }
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Write-Output 'PASS: Maximize, restore and minimize caption buttons.'

    $transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    if (-not $transform.Current.CanResize) { throw 'Window cannot resize' }
    New-Item -ItemType Directory -Path $ScreenshotDirectory -Force | Out-Null
    [ShellNative]::SetForegroundWindow($appProcess.MainWindowHandle) | Out-Null
    [ShellNative]::SetCursorPos(0, 0) | Out-Null
    Start-Sleep -Milliseconds 300
    Assert-Backdrop
    Save-Window 'launcher-shell-3.1.png'
    if ($DesktopCapture) { Save-DesktopContext }
    $normal = $window.Current.BoundingRectangle
    $sidebar = (Find-Element 'Sidebar').Current.BoundingRectangle
    if ([Math]::Abs($sidebar.Width / $normal.Width - 0.25) -gt 0.02) { throw 'Sidebar ratio is not 25%' }

    $hover = $items[1].Current.BoundingRectangle
    [ShellNative]::SetCursorPos([int]($hover.X + $hover.Width / 2), [int]($hover.Y + $hover.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 250
    Save-Window 'launcher-shell-3.1-hover.png'
    [ShellNative]::SetCursorPos(0, 0) | Out-Null

    $before = (Find-Element 'Sidebar').Current.BoundingRectangle.Width
    Drag-Splitter 80
    $after = (Find-Element 'Sidebar').Current.BoundingRectangle.Width
    if ($after -le $before + 20) { throw 'Mouse drag did not resize sidebar' }
    Assert-SidebarLimits
    Drag-Splitter 600
    Assert-SidebarLimits
    $wide = (Find-Element 'Sidebar').Current.BoundingRectangle.Width
    if ($wide / $window.Current.BoundingRectangle.Width -lt 0.34) { throw 'Did not reach sidebar maximum' }
    Save-Window 'launcher-shell-3.1-wide.png'
    Drag-Splitter -800
    Assert-SidebarLimits
    $narrow = (Find-Element 'Sidebar').Current.BoundingRectangle.Width
    if ([Math]::Abs($narrow - 210 * $dpiScale) -gt 2) { throw 'Did not reach sidebar minimum' }
    (Find-Element 'SidebarSplitter').SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{RIGHT}')
    Start-Sleep -Milliseconds 150
    if ((Find-Element 'Sidebar').Current.BoundingRectangle.Width -le $narrow) { throw 'Keyboard splitter resize failed' }
    Write-Output 'PASS: Real splitter mouse drag, 210 DIP minimum, 35% maximum and keyboard resizing.'

    $transform.Resize(800, 560)
    Start-Sleep -Milliseconds 300
    Assert-Backdrop
    Save-Window 'launcher-shell-3.1-small.png'
    $small = $window.Current.BoundingRectangle
    $sidebar = (Find-Element 'Sidebar').Current.BoundingRectangle
    Assert-SidebarLimits
    if ((Find-Element 'CloseButton').Current.IsOffscreen) { throw 'Caption clipped after resizing' }
    foreach ($item in $items) {
        if ($item.Current.IsOffscreen) { throw 'Navigation item hidden at minimum size' }
    }
    Write-Output 'PASS: Resizing, proportional sidebar and screenshot capture.'

    Invoke-Button 'CloseButton'
    if (-not $appProcess.WaitForExit(10000)) { throw 'Close button did not exit application' }
    if ($appProcess.ExitCode -ne 0) { throw "Unexpected exit code: $($appProcess.ExitCode)" }
    Write-Output 'PASS: Close button exits cleanly.'
} finally {
    if ($null -ne $appProcess) {
        if (-not $appProcess.HasExited) { $appProcess.Kill() }
        $appProcess.Dispose()
    }
    [ShellNative]::SetCursorPos($originalCursor.X, $originalCursor.Y) | Out-Null
    if ($null -ne $shell) { $shell.UndoMinimizeAll(); [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null }
}
