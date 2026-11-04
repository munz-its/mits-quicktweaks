using System.Windows;
using System.Windows.Controls;
using MunzQuickTweaks.App.ViewModels;

namespace MunzQuickTweaks.App.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // Der Beschreibungsschlüssel kommt aus dem ViewModel, der Text bleibt sprachdynamisch
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is PlaceholderViewModel vm)
        {
            Description.SetResourceReference(TextBlock.TextProperty, vm.DescriptionKey);
        }
    }
}
