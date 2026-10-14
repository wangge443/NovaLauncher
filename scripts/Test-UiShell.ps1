param([string]$ScreenshotDirectory = (Join-Path $PSScriptRoot '../docs/screenshots'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShellNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@
[ShellNative]::SetProcessDPIAware() | Out-Null
$exe = Join-Path $PSScriptRoot '../NovaLauncher.App/bin/Release/net9.0-windows/NovaLauncher.App.exe'
$appProcess = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden

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
    $rect = New-Object ShellNative+RECT
    [ShellNative]::GetWindowRect($appProcess.MainWindowHandle, [ref]$rect) | Out-Null
    $bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try {
        if (-not [ShellNative]::PrintWindow($appProcess.MainWindowHandle, $dc, 2)) { throw 'Window capture failed' }
    } finally { $graphics.ReleaseHdc($dc) }
    try { $bitmap.Save((Join-Path $ScreenshotDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

try {
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

    $navigation = Find-Element 'SidebarNavigation'
    $items = $navigation.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem))
    if ($items.Count -ne 6) { throw "Expected six navigation items, found $($items.Count)" }
    foreach ($item in $items) {
        $selection = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        Start-Sleep -Milliseconds 100
        if ((Find-Element 'ContentTitle').Current.Name -ne $item.Current.Name) { throw 'Navigation binding failed' }
    }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-Output 'PASS: Six navigation items select and update the content heading.'

    $items[0].SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    Start-Sleep -Milliseconds 150
    if ((Find-Element 'ContentTitle').Current.Name -ne '游戏') { throw 'Keyboard navigation failed' }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-Output 'PASS: Keyboard down-arrow navigation.'

    $bounds = $window.Current.BoundingRectangle
    $captionPoint = [IntPtr](([int]($bounds.Y + 20) -shl 16) -bor [int]($bounds.X + 300))
    $hit = [ShellNative]::SendMessage($appProcess.MainWindowHandle, 0x84, [IntPtr]::Zero, $captionPoint)
    if ($hit.ToInt32() -ne 2) { throw 'Title bar is not a native draggable caption' }
    Write-Output 'PASS: Native title-bar drag hit target.'

    # Exercise the actual caption buttons, not only WindowPattern state changes.
    Invoke-Button 'MaximizeButton'
    if ($windowPattern.Current.WindowVisualState -ne 'Maximized') { throw 'Maximize failed' }
    if ((Find-Element 'MaximizeButton').Current.Name -ne '还原') { throw 'Restore label failed' }
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
    Save-Window 'launcher-shell.png'
    $normal = $window.Current.BoundingRectangle
    $sidebar = (Find-Element 'Sidebar').Current.BoundingRectangle
    if ([Math]::Abs($sidebar.Width / $normal.Width - 0.25) -gt 0.02) { throw 'Sidebar ratio is not 25%' }

    $hover = $items[1].Current.BoundingRectangle
    [ShellNative]::SetCursorPos([int]($hover.X + $hover.Width / 2), [int]($hover.Y + $hover.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 250
    Save-Window 'launcher-shell-hover.png'
    [ShellNative]::SetCursorPos(0, 0) | Out-Null

    $transform.Resize(800, 560)
    Start-Sleep -Milliseconds 300
    Save-Window 'launcher-shell-small.png'
    $small = $window.Current.BoundingRectangle
    $sidebar = (Find-Element 'Sidebar').Current.BoundingRectangle
    if ([Math]::Abs($sidebar.Width / $small.Width - 0.25) -gt 0.02) { throw 'Resized sidebar ratio changed' }
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
    if (-not $appProcess.HasExited) { $appProcess.Kill() }
    $appProcess.Dispose()
}
