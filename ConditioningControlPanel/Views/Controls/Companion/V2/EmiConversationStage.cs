using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Views.Controls.Companion.V2;

/// <summary>The shipped EMI art and face geometry, composed without a second mascot window.</summary>
public sealed class EmiConversationStage : Viewbox
{
    private readonly EmiFace _face;
    public static readonly DependencyProperty FaceProperty = DependencyProperty.Register(nameof(Face), typeof(string),
        typeof(EmiConversationStage), new PropertyMetadata("^_^", (d, e) => ((EmiConversationStage)d).SetFace((string)e.NewValue)));
    public string Face { get => (string)GetValue(FaceProperty); set => SetValue(FaceProperty, value); }
    public EmiConversationStage()
    {
        IsHitTestVisible = false;
        Stretch = Stretch.Uniform;
        var canvas = new Canvas { Width = 859, Height = 869 };
        var image = new Image { Width = 859, Height = 869, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var file = Path.Combine(AppContext.BaseDirectory, "Resources", "web", "arcademy", "art", "emi", "body-idle.png");
        try
        {
            if (File.Exists(file))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(file); bitmap.EndInit(); bitmap.Freeze();
                image.Source = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException)
        {
            App.Logger?.Warning("Companion stage art could not load: {Type}", ex.GetType().Name);
        }
        canvas.Children.Add(image);
        _face = new EmiFace { Width = 859 * .4168, Height = 869 * .3763 };
        Canvas.SetLeft(_face, 859 * .3446); Canvas.SetTop(_face, 869 * .2946);
        canvas.Children.Add(_face);
        Child = canvas;
        SetFace(Face);
    }
    private void SetFace(string face) { if (_face != null) _face.Face = face; }
}