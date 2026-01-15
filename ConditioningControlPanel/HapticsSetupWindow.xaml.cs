using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel;

public partial class HapticsSetupWindow : Window
{
    private enum Provider { None, Lovense, Buttplug }
    private Provider _selectedProvider = Provider.None;
    private int _currentSlide = 1;
    private const int TotalSlides = 3;

    public HapticsSetupWindow()
    {
        InitializeComponent();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnSelectLovense_Click(object sender, RoutedEventArgs e)
    {
        _selectedProvider = Provider.Lovense;
        _currentSlide = 1;
        ShowTutorial();
    }

    private void BtnSelectButtplug_Click(object sender, RoutedEventArgs e)
    {
        _selectedProvider = Provider.Buttplug;
        _currentSlide = 1;
        ShowTutorial();
    }

    private void ShowTutorial()
    {
        ProviderSelectionGrid.Visibility = Visibility.Collapsed;

        if (_selectedProvider == Provider.Lovense)
        {
            TxtTitle.Text = "Lovense Setup Guide";
            TxtTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4")!);
            LovenseSlides.Visibility = Visibility.Visible;
            ButtplugSlides.Visibility = Visibility.Collapsed;

            // Set indicator colors for Lovense (pink)
            SetIndicatorColor(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4")!));
            BtnNext.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4")!);
        }
        else
        {
            TxtTitle.Text = "Buttplug.io Setup Guide";
            TxtTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9B59B6")!);
            LovenseSlides.Visibility = Visibility.Collapsed;
            ButtplugSlides.Visibility = Visibility.Visible;

            // Set indicator colors for Buttplug (purple)
            SetIndicatorColor(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9B59B6")!));
            BtnNext.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9B59B6")!);
        }

        ShowNavigationControls();
        UpdateSlideVisibility();
    }

    private void SetIndicatorColor(Brush activeColor)
    {
        // Store the active color for use in UpdateSlideIndicators
        Dot1.Tag = activeColor;
    }

    private void ShowNavigationControls()
    {
        BtnPrevious.Visibility = Visibility.Visible;
        BtnNext.Visibility = Visibility.Visible;
        SlideIndicators.Visibility = Visibility.Visible;
    }

    private void BtnPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSlide == 1)
        {
            // Go back to provider selection
            _selectedProvider = Provider.None;
            ProviderSelectionGrid.Visibility = Visibility.Visible;
            LovenseSlides.Visibility = Visibility.Collapsed;
            ButtplugSlides.Visibility = Visibility.Collapsed;
            BtnPrevious.Visibility = Visibility.Collapsed;
            BtnNext.Visibility = Visibility.Collapsed;
            BtnDone.Visibility = Visibility.Collapsed;
            SlideIndicators.Visibility = Visibility.Collapsed;
            TxtTitle.Text = "Haptics Setup Guide";
            TxtTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4")!);
        }
        else
        {
            _currentSlide--;
            UpdateSlideVisibility();
        }
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSlide < TotalSlides)
        {
            _currentSlide++;
            UpdateSlideVisibility();
        }
    }

    private void UpdateSlideVisibility()
    {
        // Update navigation buttons
        BtnPrevious.Content = _currentSlide == 1 ? "Back" : "Previous";

        if (_currentSlide == TotalSlides)
        {
            BtnNext.Visibility = Visibility.Collapsed;
            BtnDone.Visibility = Visibility.Visible;
        }
        else
        {
            BtnNext.Visibility = Visibility.Visible;
            BtnDone.Visibility = Visibility.Collapsed;
        }

        // Update slide indicators
        var activeColor = Dot1.Tag as Brush ?? Brushes.White;
        var inactiveColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#404060")!);

        Dot1.Fill = _currentSlide >= 1 ? activeColor : inactiveColor;
        Dot2.Fill = _currentSlide >= 2 ? activeColor : inactiveColor;
        Dot3.Fill = _currentSlide >= 3 ? activeColor : inactiveColor;

        // Show/hide appropriate slides
        if (_selectedProvider == Provider.Lovense)
        {
            LovenseSlide1.Visibility = _currentSlide == 1 ? Visibility.Visible : Visibility.Collapsed;
            LovenseSlide2.Visibility = _currentSlide == 2 ? Visibility.Visible : Visibility.Collapsed;
            LovenseSlide3.Visibility = _currentSlide == 3 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ButtplugSlide1.Visibility = _currentSlide == 1 ? Visibility.Visible : Visibility.Collapsed;
            ButtplugSlide2.Visibility = _currentSlide == 2 ? Visibility.Visible : Visibility.Collapsed;
            ButtplugSlide3.Visibility = _currentSlide == 3 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
