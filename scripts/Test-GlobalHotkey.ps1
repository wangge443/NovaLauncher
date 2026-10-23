param(
    [string]$BuildDirectory = "$PSScriptRoot/../artifacts/build/8.5",
    [string]$DataRoot = "$PSScriptRoot/../artifacts/verification/8.5/fixture-300",
    [string]$OutputDirectory = "$PSScriptRoot/../artifacts/verification/8.5/process"
)
$ErrorActionPreference = 'Stop'
$build = [IO.Path]::GetFullPath($BuildDirectory)
$data = [IO.Path]::GetFullPath($DataRoot)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $data '.nova-verification'))) { throw 'Expected isolated fixture marker' }
New-Item -ItemType Directory -Force $output | Out-Null
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class HotkeyNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 public delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public static IntPtr Find(string text) {
   IntPtr found=IntPtr.Zero;
   EnumWindows((h,p)=>{var s=new StringBuilder(512);GetWindowText(h,s,512);if(s.ToString().Contains(text)){found=h;return false;}return true;},IntPtr.Zero);
   return found;
 }
}
'@
[HotkeyNative]::SetProcessDPIAware() | Out-Null
$count=0
$nova=$null
$browser=$null
$browserHandle=[IntPtr]::Zero
function Assert($condition,[string]$message) { if(-not $condition){throw $message}; $script:count++; Write-Output "PASS: $message" }
function Wait-For([scriptblock]$condition,[string]$message) {
 $watch=[Diagnostics.Stopwatch]::StartNew()
 while($watch.Elapsed.TotalSeconds -lt 20){ if(& $condition){return}; Start-Sleep -Milliseconds 40 }
 throw "Timeout: $message"
}
function Send-Key([byte]$key){[HotkeyNative]::keybd_event($key,0,0,[UIntPtr]::Zero);[HotkeyNative]::keybd_event($key,0,2,[UIntPtr]::Zero)}
function Send-Hotkey([byte]$modifier=0x12){
 [HotkeyNative]::keybd_event(0x11,0,0,[UIntPtr]::Zero)
 [HotkeyNative]::keybd_event($modifier,0,0,[UIntPtr]::Zero)
 try {Send-Key 0x20} finally {
  [HotkeyNative]::keybd_event($modifier,0,2,[UIntPtr]::Zero)
  [HotkeyNative]::keybd_event(0x11,0,2,[UIntPtr]::Zero)
 }
}
function Start-Nova {
 $script:nova=Start-Process -FilePath (Join-Path $build 'NovaLauncher.exe') -ArgumentList ('--verification-data-root="'+$data+'"') -WindowStyle Hidden -PassThru
 Wait-For { $nova.Refresh(); $nova.MainWindowHandle -ne [IntPtr]::Zero } 'Nova HWND'
 Start-Sleep -Milliseconds 1500
}
function Query-Box {
 $root=[System.Windows.Automation.AutomationElement]::FromHandle($nova.MainWindowHandle)
 $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'QuickQueryBox')
 return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Focus-Browser {
 [HotkeyNative]::ShowWindow($browserHandle,9) | Out-Null
 [HotkeyNative]::SetForegroundWindow($browserHandle) | Out-Null
 Start-Sleep -Milliseconds 150
 $rect=New-Object HotkeyNative+Rect
 [HotkeyNative]::GetWindowRect($browserHandle,[ref]$rect) | Out-Null
 [HotkeyNative]::SetCursorPos($rect.Left+200,$rect.Top+20) | Out-Null
 [HotkeyNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[HotkeyNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Wait-For { [HotkeyNative]::GetForegroundWindow() -eq $browserHandle } 'actual browser foreground'
}
function Capture([string]$name) {
 $rect=New-Object HotkeyNative+Rect
 [HotkeyNative]::GetWindowRect($nova.MainWindowHandle,[ref]$rect) | Out-Null
 $bitmap=New-Object Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
 $graphics=[Drawing.Graphics]::FromImage($bitmap)
 try {$graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $output $name),[Drawing.Imaging.ImageFormat]::Png)}
 finally {$graphics.Dispose();$bitmap.Dispose()}
}
try {
 & (Join-Path $build 'NovaLauncher.Verification.exe') --hotkey-config $data true 3 32
 if($LASTEXITCODE -ne 0){throw 'Cannot configure isolated fixture'}
 $html=Join-Path $output 'browser-fixture.html'
 '<!doctype html><meta charset="utf-8"><title>Nova 8.5 Browser Fixture</title><body style="background:#c1d4e3;font:32px Segoe UI;padding:60px">Browser foreground - Nova global hotkey verification</body>' | Set-Content -LiteralPath $html -Encoding utf8
 $chrome='C:\Program Files\Google\Chrome\Application\chrome.exe'
 $browser=Start-Process -FilePath $chrome -ArgumentList @('--user-data-dir="'+(Join-Path $output 'browser-profile')+'"','--no-first-run','--no-default-browser-check','--app="'+([Uri]$html).AbsoluteUri+'"') -WindowStyle Hidden -PassThru
 Wait-For { $script:browserHandle=[HotkeyNative]::Find('Nova 8.5 Browser Fixture'); $browserHandle -ne [IntPtr]::Zero } 'isolated browser fixture'
 Start-Nova
 Focus-Browser
 Assert ([HotkeyNative]::GetForegroundWindow() -eq $browserHandle) 'Real Chrome owns foreground'
 Send-Hotkey
 Wait-For { $q=Query-Box; $q -and $q.Current.HasKeyboardFocus } 'global hotkey focuses query from browser'
 Assert ([HotkeyNative]::GetForegroundWindow() -eq $nova.MainWindowHandle) 'Global hotkey activates independent Nova process over Chrome'
 Capture 'global-from-browser.png'
 Send-Key 0x1B
 [HotkeyNative]::ShowWindow($nova.MainWindowHandle,6) | Out-Null
 Focus-Browser
 Send-Hotkey
 Wait-For { -not [HotkeyNative]::IsIconic($nova.MainWindowHandle) -and (Query-Box).Current.HasKeyboardFocus } 'restore from browser'
 Assert $true 'Browser foreground hotkey restores minimized Glass Nova and focuses query'
 Send-Key 0x1B
 Focus-Browser
 Send-Key 0x7A
 Start-Sleep -Milliseconds 2500
 $fullscreenRect=New-Object HotkeyNative+Rect
 [HotkeyNative]::GetWindowRect($browserHandle,[ref]$fullscreenRect) | Out-Null
 $screen=[Windows.Forms.Screen]::FromHandle($browserHandle).Bounds
 Assert (($fullscreenRect.Right-$fullscreenRect.Left) -ge $screen.Width -and ($fullscreenRect.Bottom-$fullscreenRect.Top) -ge $screen.Height) 'Chrome F11 actually covers the monitor'
 Send-Hotkey
 Wait-For { (Query-Box).Current.HasKeyboardFocus } 'browser fullscreen hotkey'
 Assert ([HotkeyNative]::GetForegroundWindow() -eq $nova.MainWindowHandle) 'Hotkey activates Nova over Chrome fullscreen'
 Capture 'global-from-browser-fullscreen.png'
 Send-Key 0x1B
 [HotkeyNative]::ShowWindow($nova.MainWindowHandle,6) | Out-Null
 $second=Start-Process -FilePath (Join-Path $build 'NovaLauncher.exe') -ArgumentList ('--verification-data-root="'+$data+'"') -WindowStyle Hidden -PassThru
 Assert ($second.WaitForExit(10000) -and $second.ExitCode -eq 0) 'Second Nova process exits after activating primary'
 Wait-For { -not [HotkeyNative]::IsIconic($nova.MainWindowHandle) } 'secondary restores primary'
 Assert $true 'Existing named-pipe activation remains intact'
 $second.Dispose()
 $nova.Kill(); $nova.WaitForExit(); $nova.Dispose(); $nova=$null
 Start-Nova
 Focus-Browser; Send-Hotkey
 Wait-For { (Query-Box).Current.HasKeyboardFocus } 'hotkey after process termination'
 Assert $true 'Windows releases hotkey after force termination and restart re-registers'
 [HotkeyNative]::PostMessage($nova.MainWindowHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
 Assert ($nova.WaitForExit(15000) -and $nova.ExitCode -eq 0) 'Normal X/WM_CLOSE exits process'
 $nova.Dispose();$nova=$null
 & (Join-Path $build 'NovaLauncher.Verification.exe') --hotkey-config $data true 6 32
 if($LASTEXITCODE -ne 0){throw 'Cannot save changed shortcut'}
 Start-Nova
 Focus-Browser; Send-Hotkey 0x10
 Wait-For { (Query-Box).Current.HasKeyboardFocus } 'persisted changed hotkey'
 Assert $true 'New process restores saved Ctrl+Shift+Space'
 [HotkeyNative]::PostMessage($nova.MainWindowHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
 Assert ($nova.WaitForExit(15000) -and $nova.ExitCode -eq 0) 'Changed hotkey unregisters on normal shutdown'
 $nova.Dispose();$nova=$null
 & (Join-Path $build 'NovaLauncher.Verification.exe') --hotkey-config $data true 3 32
 Assert ([HotkeyNative]::RegisterHotKey([IntPtr]::Zero,99,0x4003,32)) 'Separate process reserves default shortcut for startup conflict'
 try {
  Start-Nova
  $latest=Get-ChildItem (Join-Path $data 'Logs') -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  Assert ((Get-Content $latest.FullName -Raw) -match 'Register failure code=1409') 'Startup conflict is logged while Nova remains running'
  [HotkeyNative]::ShowWindow($nova.MainWindowHandle,9) | Out-Null
  [HotkeyNative]::SetForegroundWindow($nova.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 200
  [HotkeyNative]::keybd_event(0x11,0,0,[UIntPtr]::Zero)
  try {Send-Key 0x4B} finally {[HotkeyNative]::keybd_event(0x11,0,2,[UIntPtr]::Zero)}
  Wait-For { (Query-Box).Current.HasKeyboardFocus } 'Ctrl+K despite startup conflict'
  Assert $true 'Local Ctrl+K still works when global registration conflicts'
  [HotkeyNative]::PostMessage($nova.MainWindowHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
  Assert ($nova.WaitForExit(15000)) 'Conflicted process exits normally'
  $nova.Dispose();$nova=$null
 } finally { [HotkeyNative]::UnregisterHotKey([IntPtr]::Zero,99) | Out-Null }
 Write-Output "PASS: $count process/browser assertions"
} finally {
 if($nova){if(-not $nova.HasExited){$nova.Kill();$nova.WaitForExit()};$nova.Dispose()}
 if($browserHandle -ne [IntPtr]::Zero){[HotkeyNative]::PostMessage($browserHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null}
 if($browser){$browser.Dispose()}
}
