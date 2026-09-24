param([string]$AssemblyPath = "$PSScriptRoot/../bin/Debug/net8.0-windows10.0.19041.0/win-x64/ConditioningControlPanel.dll")
$ErrorActionPreference = 'Stop'
# Existing art plus the real face renderer. No app startup or API calls.
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$appRoot = (Resolve-Path -LiteralPath "$PSScriptRoot/..").Path
$renderRoot = Join-Path ([IO.Path]::GetTempPath()) ('ccp-emi-tube-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $renderRoot | Out-Null
$escapedAssembly = [Security.SecurityElement]::Escape($assembly)
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows10.0.19041.0</TargetFramework><UseWPF>true</UseWPF><WindowsSdkPackageVersion>10.0.26100.84</WindowsSdkPackageVersion><RuntimeIdentifier>win-x64</RuntimeIdentifier></PropertyGroup>
<ItemGroup><Reference Include="ConditioningControlPanel"><HintPath>$escapedAssembly</HintPath><Private>false</Private></Reference></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $renderRoot 'Render.csproj'), $project)
$source = @'
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class Program
{
    [STAThread] static void Main(string[] args)
    {
        var bin = Path.GetDirectoryName(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            var path = Path.Combine(bin, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Render(args[1], args[2]);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void Render(string root, string previewPath)
    {
        var app = new Application();
        var bodies = new[] { "idle", "smug", "sad", "pet", "shock", "sway2", "idle", "sad" };
        var faces = new[] { "^_^", "^_~", "T_T", "^___^", "o_o", "GG", "-_-", "x_x" };
        var output = Path.Combine(root, "Resources", "emi", "tube");
        Directory.CreateDirectory(output);
        var preview = new Canvas { Width = 800, Height = 440, Background = new SolidColorBrush(Color.FromRgb(24, 20, 35)) };
        for (var i = 0; i < bodies.Length; i++)
        {
            var source = new BitmapImage(new Uri(Path.Combine(root, "Resources", "web", "arcademy", "art", "emi", "body-" + bodies[i] + ".png")));
            var width = source.PixelWidth; var height = source.PixelHeight;
            var canvas = new Canvas { Width = width, Height = height };
            canvas.Children.Add(new Image { Source = source, Width = width, Height = height });
            var face = new ConditioningControlPanel.Services.EmiDesk.EmiFace {
                Width = width * .4168, Height = height * .3763, Face = faces[i] };
            Canvas.SetLeft(face, width * .3446); Canvas.SetTop(face, height * .2946);
            canvas.Children.Add(face);
            canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var tile = new Image { Source = bitmap, Width = 190, Height = 200, Stretch = Stretch.Uniform };
            Canvas.SetLeft(tile, i % 4 * 200 + 5); Canvas.SetTop(tile, i / 4 * 220);
            preview.Children.Add(tile);
            var label = new TextBlock { Text = (i + 1) + ": " + faces[i], Foreground = Brushes.White };
            Canvas.SetLeft(label, i % 4 * 200 + 65); Canvas.SetTop(label, i / 4 * 220 + 202);
            preview.Children.Add(label);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, "pose" + (i + 1) + ".png"));
            encoder.Save(stream);
        }
        preview.Measure(new Size(800, 440)); preview.Arrange(new Rect(0, 0, 800, 440)); preview.UpdateLayout();
        var sheet = new RenderTargetBitmap(800, 440, 96, 96, PixelFormats.Pbgra32); sheet.Render(preview);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(sheet));
        using (var stream = File.Create(previewPath)) png.Save(stream);
        app.Shutdown();
        Console.WriteLine(output);
    }
}
'@
[IO.File]::WriteAllText((Join-Path $renderRoot 'Program.cs'), $source)
dotnet build (Join-Path $renderRoot 'Render.csproj') --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Renderer build failed' }
$bin = Join-Path $renderRoot 'bin/Debug/net8.0-windows10.0.19041.0/win-x64'
$fontDir = Join-Path $bin 'Resources/emi/fonts'
New-Item -ItemType Directory -Path $fontDir -Force | Out-Null
Copy-Item -Path "$appRoot/Resources/emi/fonts/*.ttf" -Destination $fontDir
$previousUserData = $env:CCP_USERDATA_DIR
try {
    $env:CCP_USERDATA_DIR = Join-Path $renderRoot 'isolated-userdata'
    dotnet (Join-Path $bin 'Render.dll') $assembly $appRoot (Join-Path $renderRoot 'contact.png')
    if ($LASTEXITCODE -ne 0) { throw 'Renderer failed' }
} finally {
    $env:CCP_USERDATA_DIR = $previousUserData
}

Write-Output (Join-Path $renderRoot 'contact.png')
