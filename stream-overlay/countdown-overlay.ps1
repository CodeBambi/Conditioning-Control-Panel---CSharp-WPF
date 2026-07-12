<#
    Conditioning Control Panel — Movable Stream Countdown Overlay
    -------------------------------------------------------------
    A borderless, transparent, always-on-top window you can drag
    anywhere on screen (like the avatar / tube). Local only.

    USAGE (from PowerShell in this folder):
        .\countdown-overlay.ps1                  # 15 min countdown
        .\countdown-overlay.ps1 -Minutes 10
        .\countdown-overlay.ps1 -To "20:30"      # count to 8:30 PM (24h clock)
        .\countdown-overlay.ps1 -Title "Back in" -Sub "Grab a drink"

    CONTROLS:
        Left-drag  ....... move the overlay anywhere
        Mouse wheel ...... resize (bigger / smaller)
        Esc  /  right-click close
#>

param(
    [double]$Minutes = 15,
    [string]$To      = "",
    [string]$Title   = "Stream starting in",
    [string]$Sub     = "Good girls wait patiently",
    [string]$LiveText = "WE'RE LIVE",
    [string]$LogoPath = "$PSScriptRoot\..\ConditioningControlPanel\Resources\logo.png"
)

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

# ---- resolve target end time ----
$now = Get-Date
if ($To -match '^\d{1,2}:\d{2}$') {
    $parts = $To.Split(':')
    $end = $now.Date.AddHours([int]$parts[0]).AddMinutes([int]$parts[1])
    if ($end -le $now) { $end = $end.AddDays(1) }   # next day if already past
} else {
    $end = $now.AddMinutes($Minutes)
}

# ---- resolve logo (embed via absolute file uri; skip gracefully if missing) ----
$logoUri = ""
try {
    $resolved = (Resolve-Path -LiteralPath $LogoPath -ErrorAction Stop).Path
    $logoUri  = ([Uri]$resolved).AbsoluteUri
} catch { $logoUri = "" }

$logoXaml = if ($logoUri) {
@"
        <Image x:Name="Logo" Width="220" Height="220" Stretch="Uniform" Source="$logoUri">
          <Image.Effect><DropShadowEffect Color="#FF2E97" BlurRadius="34" ShadowDepth="0" Opacity="0.9"/></Image.Effect>
        </Image>
"@
} else { "" }

[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="WidthAndHeight" WindowStartupLocation="CenterScreen">
  <Grid x:Name="Root" Margin="60">
    <Grid.LayoutTransform><ScaleTransform x:Name="Scale" ScaleX="1" ScaleY="1"/></Grid.LayoutTransform>

    <!-- soft glow puddle -->
    <Ellipse Width="520" Height="520" HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,-40,0,0">
      <Ellipse.Fill>
        <RadialGradientBrush>
          <GradientStop Color="#66FF2E97" Offset="0"/>
          <GradientStop Color="#33B76BFF" Offset="0.45"/>
          <GradientStop Color="#00000000" Offset="0.75"/>
        </RadialGradientBrush>
      </Ellipse.Fill>
      <Ellipse.Effect><BlurEffect Radius="30"/></Ellipse.Effect>
    </Ellipse>

    <StackPanel x:Name="Stack" HorizontalAlignment="Center" VerticalAlignment="Center">
      <StackPanel.RenderTransform><TranslateTransform x:Name="Float" Y="0"/></StackPanel.RenderTransform>

      <!-- kicker -->
      <TextBlock x:Name="Kicker" Text="STREAM STARTING IN" HorizontalAlignment="Center"
                 FontFamily="Segoe UI" FontSize="26" FontWeight="SemiBold"
                 Foreground="#FFD7EF" Margin="0,0,0,14">
        <TextBlock.Effect><DropShadowEffect Color="#FF2E97" BlurRadius="18" ShadowDepth="0" Opacity="0.95"/></TextBlock.Effect>
      </TextBlock>

      <!-- centerpiece: rotating rings + logo -->
      <Grid Width="300" Height="300" HorizontalAlignment="Center">
        <Ellipse Width="288" Height="288" Stroke="#CCFF69B4" StrokeThickness="3"
                 StrokeDashArray="1 5" RenderTransformOrigin="0.5,0.5">
          <Ellipse.RenderTransform><RotateTransform x:Name="Ring1" Angle="0"/></Ellipse.RenderTransform>
          <Ellipse.Effect><DropShadowEffect Color="#FF69B4" BlurRadius="12" ShadowDepth="0"/></Ellipse.Effect>
        </Ellipse>
        <Ellipse Width="256" Height="256" Stroke="#99B76BFF" StrokeThickness="2"
                 StrokeDashArray="2 9" RenderTransformOrigin="0.5,0.5">
          <Ellipse.RenderTransform><RotateTransform x:Name="Ring2" Angle="0"/></Ellipse.RenderTransform>
        </Ellipse>
$logoXaml
      </Grid>

      <!-- countdown -->
      <TextBlock x:Name="Count" Text="--:--" HorizontalAlignment="Center"
                 FontFamily="Segoe UI" FontSize="120" FontWeight="Bold" Margin="0,6,0,0">
        <TextBlock.Foreground>
          <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
            <GradientStop Color="#FFFFFFFF" Offset="0"/>
            <GradientStop Color="#FFFFD0EC" Offset="0.35"/>
            <GradientStop Color="#FFFF2E97" Offset="0.72"/>
            <GradientStop Color="#FFB76BFF" Offset="1"/>
          </LinearGradientBrush>
        </TextBlock.Foreground>
        <TextBlock.Effect><DropShadowEffect Color="#FF2E97" BlurRadius="28" ShadowDepth="0" Opacity="0.9"/></TextBlock.Effect>
      </TextBlock>

      <!-- subtitle -->
      <TextBlock x:Name="SubLine" Text="GOOD GIRLS WAIT PATIENTLY" HorizontalAlignment="Center"
                 FontFamily="Segoe UI" FontSize="18" FontWeight="Medium"
                 Foreground="#E9C8FF" Margin="0,10,0,0">
        <TextBlock.Effect><DropShadowEffect Color="#7B2FF7" BlurRadius="14" ShadowDepth="0"/></TextBlock.Effect>
      </TextBlock>
    </StackPanel>
  </Grid>

  <Window.Triggers>
    <EventTrigger RoutedEvent="Window.Loaded">
      <BeginStoryboard>
        <Storyboard>
          <DoubleAnimation Storyboard.TargetName="Ring1" Storyboard.TargetProperty="Angle"
                           From="0" To="360" Duration="0:0:16" RepeatBehavior="Forever"/>
          <DoubleAnimation Storyboard.TargetName="Ring2" Storyboard.TargetProperty="Angle"
                           From="360" To="0" Duration="0:0:22" RepeatBehavior="Forever"/>
          <DoubleAnimation Storyboard.TargetName="Float" Storyboard.TargetProperty="Y"
                           From="0" To="-14" Duration="0:0:2.75" AutoReverse="True" RepeatBehavior="Forever"/>
        </Storyboard>
      </BeginStoryboard>
    </EventTrigger>
  </Window.Triggers>
</Window>
"@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$win    = [Windows.Markup.XamlReader]::Load($reader)

$kickerText = $Title.ToUpper()
$subText    = $Sub.ToUpper()

$tbCount  = $win.FindName("Count")
$tbKicker = $win.FindName("Kicker")
$tbSub    = $win.FindName("SubLine")
$scale    = $win.FindName("Scale")

$tbKicker.Text = $kickerText
$tbSub.Text    = $subText

# ---- drag to move ----
$win.Add_MouseLeftButtonDown({ try { $win.DragMove() } catch {} })

# ---- wheel to resize ----
$win.Add_MouseWheel({
    param($s,$e)
    $step = if ($e.Delta -gt 0) { 0.08 } else { -0.08 }
    $newS = [Math]::Max(0.4, [Math]::Min(2.5, $scale.ScaleX + $step))
    $scale.ScaleX = $newS; $scale.ScaleY = $newS
})

# ---- esc / right-click to close ----
$win.Add_KeyDown({ param($s,$e) if ($e.Key -eq 'Escape') { $win.Close() } })
$win.Add_MouseRightButtonUp({ $win.Close() })

# ---- countdown tick ----
$script:live = $false
$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(250)
$timer.Add_Tick({
    $ms = ($end - (Get-Date)).TotalSeconds
    if ($ms -le 0) {
        if (-not $script:live) {
            $script:live = $true
            $tbKicker.Visibility = 'Collapsed'
            $tbSub.Visibility    = 'Collapsed'
            $tbCount.FontSize    = 84
            $tbCount.Text        = $LiveText
        }
        return
    }
    $t = [int][Math]::Floor($ms)
    $h = [int]($t / 3600); $m = [int](($t % 3600) / 60); $s = $t % 60
    if ($h -gt 0) { $tbCount.Text = "{0}:{1:00}:{2:00}" -f $h,$m,$s }
    else          { $tbCount.Text = "{0:00}:{1:00}" -f $m,$s }
})
$timer.Start()

$win.ShowDialog() | Out-Null
