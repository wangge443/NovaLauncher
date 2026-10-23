param(
 [string]$PublishDirectory="$PSScriptRoot/../artifacts/publish/self-contained",
 [string]$DataRoot="$PSScriptRoot/../artifacts/verification/8.6/published-fixture",
 [string]$OutputDirectory="$PSScriptRoot/../docs/screenshots/8.6"
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
Add-Type @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class TrayNative {
 public delegate bool CB(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c,IntPtr t);
 [DllImport("user32.dll")] public static extern bool EnumWindows(CB c,IntPtr p);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder t,int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr p);
 [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr p);
 [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
 public static IntPtr[] Windows(int pid,bool visible) {var a=new List<IntPtr>();EnumWindows((h,p)=>{uint id;GetWindowThreadProcessId(h,out id);if(id==pid&&(!visible||IsWindowVisible(h)))a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
 public static IntPtr MainWindow(int pid){foreach(var h in Windows(pid,false)){var s=new StringBuilder(512);GetWindowText(h,s,512);if(s.ToString()=="Nova Launcher")return h;}return IntPtr.Zero;}
}
'@
[TrayNative]::SetProcessDPIAware()|Out-Null
$data=[IO.Path]::GetFullPath($DataRoot);$output=[IO.Path]::GetFullPath($OutputDirectory);$exe=Join-Path ([IO.Path]::GetFullPath($PublishDirectory)) 'NovaLauncher.exe'
New-Item -ItemType Directory -Force $data,$output|Out-Null
if(-not(Test-Path (Join-Path $data '.nova-verification'))){if((Get-ChildItem $data -Force).Count){throw 'Use a marked isolated fixture'};Set-Content (Join-Path $data '.nova-verification') ''}
$appProcess=$null;$count=0
function Assert($ok,$message){if(-not $ok){throw $message};$script:count++;"PASS: $message"}
function Wait-For([scriptblock]$condition,$message){$w=[Diagnostics.Stopwatch]::StartNew();while($w.Elapsed.TotalSeconds -lt 20){if(& $condition){return};Start-Sleep -Milliseconds 60};throw "Timeout: $message"}
function Key([byte]$k){[TrayNative]::keybd_event($k,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 50;[TrayNative]::keybd_event($k,0,2,[UIntPtr]::Zero)}
function Hotkey{[TrayNative]::keybd_event(0x11,0,0,[UIntPtr]::Zero);[TrayNative]::keybd_event(0x12,0,0,[UIntPtr]::Zero);try{Key 0x20}finally{[TrayNative]::keybd_event(0x12,0,2,[UIntPtr]::Zero);[TrayNative]::keybd_event(0x11,0,2,[UIntPtr]::Zero)}}
function Click($element,[switch]$Right){$r=$element.Current.BoundingRectangle;if($r.IsEmpty){throw ("Element has no screen rectangle: "+$element.Current.Name+" "+$element.Current.ControlType.ProgrammaticName)};[TrayNative]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2))|Out-Null;Start-Sleep -Milliseconds 100;$down=2;$up=4;if($Right){$down=8;$up=16};[TrayNative]::mouse_event($down,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 250;[TrayNative]::mouse_event($up,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 180}
function Find-In($handle,$name,[switch]$Id,[switch]$Button,[switch]$MenuItem){if($handle -eq [IntPtr]::Zero){return $null};$r=[System.Windows.Automation.AutomationElement]::FromHandle($handle);$property=[System.Windows.Automation.AutomationElement]::NameProperty;if($Id){$property=[System.Windows.Automation.AutomationElement]::AutomationIdProperty};$c=New-Object System.Windows.Automation.PropertyCondition($property,$name);if($Button -or $MenuItem){$type=[System.Windows.Automation.ControlType]::Button;if($MenuItem){$type=[System.Windows.Automation.ControlType]::MenuItem};$bc=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,$type);$c=New-Object System.Windows.Automation.AndCondition($c,$bc)};return $r.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)}
function Tray-Icon {
 $bar=[TrayNative]::FindWindow('Shell_TrayWnd',[IntPtr]::Zero)
 $icon=Find-In $bar 'Nova Launcher' -Button;if($icon -and -not $icon.Current.IsOffscreen){$br=New-Object TrayNative+Rect;[TrayNative]::GetWindowRect($bar,[ref]$br)|Out-Null;$ir=$icon.Current.BoundingRectangle;if(-not $ir.IsEmpty -and $ir.Y -ge $br.Top -and $ir.Bottom -le $br.Bottom){return $icon}}
 $overflow=[TrayNative]::FindWindow('TopLevelWindowForOverflowXamlIsland',[IntPtr]::Zero)
 if($overflow -eq [IntPtr]::Zero -or -not [TrayNative]::IsWindowVisible($overflow)){
  $c=Find-In $bar 'SystemTrayIcon' -Id
  if(-not $c){throw 'Cannot locate notification overflow button'}
  Click $c
 }
 Wait-For {$script:overflowHandle=[TrayNative]::FindWindow('TopLevelWindowForOverflowXamlIsland',[IntPtr]::Zero);$overflowHandle -ne [IntPtr]::Zero -and [TrayNative]::IsWindowVisible($overflowHandle)} 'notification overflow'
 Wait-For {$script:trayButton=Find-In $overflowHandle 'Nova Launcher' -Button;$trayButton -and -not $trayButton.Current.BoundingRectangle.IsEmpty} 'tray icon layout';return $trayButton
}
function Menu-Item($name){foreach($h in [TrayNative]::Windows($appProcess.Id,$true)){$m=Find-In $h $name -MenuItem;if($m){return $m}};return $null}
function Tray-Menu($name){$icon=Tray-Icon;Start-Sleep -Milliseconds 400;Click $icon -Right;Start-Sleep -Milliseconds 500;if(-not (Menu-Item $name)){Click $icon -Right};Wait-For {$null -ne (Menu-Item $name)} "tray menu $name";Click (Menu-Item $name)}
function Capture-Rect($x,$y,$w,$h,$name){$b=New-Object Drawing.Bitmap([int]$w,[int]$h);$g=[Drawing.Graphics]::FromImage($b);try{$g.CopyFromScreen([int]$x,[int]$y,0,0,$b.Size);$b.Save((Join-Path $output $name),[Drawing.Imaging.ImageFormat]::Png)}finally{$g.Dispose();$b.Dispose()}}
function Capture-Window($h,$name){$r=New-Object TrayNative+Rect;[TrayNative]::GetWindowRect($h,[ref]$r)|Out-Null;Capture-Rect $r.Left $r.Top ($r.Right-$r.Left) ($r.Bottom-$r.Top) $name}
function Close-ToTray{[TrayNative]::PostMessage($script:main,0x10,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null;Wait-For {-not [TrayNative]::IsWindowVisible($script:main)} 'X hides main window';Start-Sleep -Milliseconds 700}
try {
 $foreground=[TrayNative]::GetForegroundWindow()
 $appProcess=Start-Process $exe -ArgumentList @('--startup',('--verification-data-root="'+$data+'"')) -WindowStyle Hidden -PassThru
 Wait-For {$script:main=[TrayNative]::MainWindow($appProcess.Id);$main -ne [IntPtr]::Zero} 'hidden HWND exists'
 Wait-For {$l=Get-ChildItem (Join-Path $data 'Logs') -Filter '*.log'|Sort-Object LastWriteTime -Descending|Select-Object -First 1;$l -and (Get-Content $l.FullName -Raw).Contains('Background Ready')} 'background ready'
 Assert (-not [TrayNative]::IsWindowVisible($main)) 'Published --startup stays hidden'
 Assert ([TrayNative]::GetForegroundWindow() -eq $foreground) 'Background startup does not steal foreground'
 $icon=Tray-Icon;Assert ($null -ne $icon) 'Real shell exposes branded Nova tray icon'
 Capture-Window $overflowHandle 'tray-icon.png'
 Click $icon
 Wait-For {[TrayNative]::IsWindowVisible($main)} 'single click shows Nova'
 Assert $true 'Tray single click activates main window'
 Close-ToTray
 $second=Start-Process $exe -ArgumentList ('--verification-data-root="'+$data+'"') -WindowStyle Hidden -PassThru
 Assert ($second.WaitForExit(10000) -and $second.ExitCode -eq 0) 'Second manual launch exits cleanly'
 Wait-For {[TrayNative]::IsWindowVisible($main)} 'hidden primary activated by IPC'
 $q=Find-In $main 'QuickQueryBox' -Id
 Assert ($null -eq $q -or $q.Current.IsOffscreen) 'Second manual launch opens full Nova without Quick Launcher'
 Close-ToTray
 $second=Start-Process $exe -ArgumentList @('--startup',('--verification-data-root="'+$data+'"')) -WindowStyle Hidden -PassThru
 Assert ($second.WaitForExit(10000) -and -not [TrayNative]::IsWindowVisible($main)) 'Duplicate --startup neither creates another instance nor shows primary'
 Hotkey
 Wait-For {$q=Find-In $main 'QuickQueryBox' -Id;$q -and $q.Current.HasKeyboardFocus} 'hotkey focuses hidden published Nova'
 Assert $true 'Published hotkey works while hidden'
 Key 0x1B;Start-Sleep -Milliseconds 500;Close-ToTray;Start-Sleep -Milliseconds 500
 Tray-Menu ((-join [char[]](0x5feb,0x901f,0x542f,0x52a8)))
 Wait-For {$q=Find-In $main 'QuickQueryBox' -Id;$q -and $q.Current.HasKeyboardFocus} 'tray quick launcher focuses query'
 Assert $true 'Tray Quick Launcher reuses focused overlay'
 Key 0x1B;Start-Sleep -Milliseconds 500;Close-ToTray;Start-Sleep -Milliseconds 500
 Tray-Menu ([string]::Concat([char]0x6253,[char]0x5f00,' Nova'))
 Wait-For {[TrayNative]::IsWindowVisible($main)} 'tray Open restores main window'
 $q=Find-In $main 'QuickQueryBox' -Id
 Assert ($null -eq $q -or $q.Current.IsOffscreen) 'Tray Open restores full window without opening overlay'
 Close-ToTray
 Tray-Menu ([string]::Concat([char]0x8bbe,[char]0x7f6e))
 Wait-For {$null -ne (Find-In $main 'StartWithWindows' -Id)} 'tray Settings opens section'
 Assert $true 'Tray Settings restores window and navigates to Settings'
 Capture-Window $main 'published-settings.png'
 $toggle=Find-In $main 'StartWithWindows' -Id
 $pattern=[System.Windows.Automation.TogglePattern]$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
 $pattern.Toggle()
 Wait-For {$pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On} 'startup toggle on'
 Assert $true 'Published Settings enables isolated startup registration'
 $pattern.Toggle()
 Wait-For {$pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off} 'startup toggle off'
 Assert $true 'Published Settings disables isolated startup registration'
 Close-ToTray
 $icon=Tray-Icon;Click $icon -Right
 $exitName=[string]::Concat([char]0x9000,[char]0x51fa,' Nova')
 Wait-For {$null -ne (Menu-Item $exitName)} 'exit menu visible'
 $menu=Menu-Item $exitName
 $r=$menu.Current.BoundingRectangle
 Capture-Rect ([Math]::Max(0,$r.X-8)) ([Math]::Max(0,$r.Y-145)) ($r.Width+16) 185 'tray-menu.png'
 Click $menu
 Assert ($appProcess.WaitForExit(15000) -and $appProcess.ExitCode -eq 0) 'Real tray Exit terminates published Nova'
 Assert ([TrayNative]::RegisterHotKey([IntPtr]::Zero,99,0x4003,32)) 'Explicit exit releases global hotkey reservation'
 [TrayNative]::UnregisterHotKey([IntPtr]::Zero,99)|Out-Null
 $appProcess.Dispose();$appProcess=$null
 Write-Output "PASS: $count published tray assertions"
} catch {Write-Output $_.ScriptStackTrace;throw} finally {if($appProcess){if(-not $appProcess.HasExited){$appProcess.Kill();$appProcess.WaitForExit()};$appProcess.Dispose()}}
