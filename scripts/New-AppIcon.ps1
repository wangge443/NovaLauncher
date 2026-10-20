# Rebuild the committed icon from the existing Sidebar N vector; no fonts or external image tools.
# Run with Windows PowerShell in STA mode: powershell -NoProfile -STA -File scripts/New-AppIcon.ps1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run this script with powershell -STA.' }
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$sidebar = Get-Content -LiteralPath (Join-Path $root 'NovaLauncher.App/Views/SidebarView.xaml') -Raw -Encoding UTF8
[xml]$theme = Get-Content -LiteralPath (Join-Path $root 'NovaLauncher.App/Themes/ShellTheme.xaml') -Raw -Encoding UTF8
$logo = $sidebar.SelectSingleNode("//*[local-name()='Path' and @Data='M 3,22 L 3,3 L 19,22 L 19,3']")
if ($null -eq $logo) { throw 'Sidebar logo changed; review icon generation before rebuilding.' }
$accent = $theme.SelectSingleNode("//*[local-name()='SolidColorBrush' and @*[local-name()='Key']='AccentBrush']").Color
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    # Opaque light tile keeps the original blue N legible on both light and dark taskbars.
    $xaml = @"
<Viewbox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="$size" Height="$size">
 <Border Width="38" Height="38" CornerRadius="12" Background="#F1F4FA" BorderBrush="#D8DFEB" BorderThickness="1">
  <Path Data="$($logo.Data)" Stroke="$accent" StrokeThickness="2.5" Width="19" Height="22" Stretch="Uniform" HorizontalAlignment="Center" VerticalAlignment="Center"/>
 </Border>
</Viewbox>
"@
    $view = [Windows.Markup.XamlReader]::Parse($xaml)
    $view.Measure([Windows.Size]::new($size, $size))
    $view.Arrange([Windows.Rect]::new(0, 0, $size, $size))
    $view.UpdateLayout()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($view)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); $frames += ,$stream.ToArray() } finally { $stream.Dispose() }
}
$output = Join-Path $root 'NovaLauncher.App/Resources/Nova.ico'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$writer = [IO.BinaryWriter]::new([IO.File]::Create($output))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
Write-Host "Generated $output ($($sizes -join ', ') px)"
