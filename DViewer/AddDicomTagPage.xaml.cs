// AddDicomTagPage.xaml.cs
using Microsoft.Maui.Controls;

namespace DViewer;

public partial class AddDicomTagPage : ContentPage
{
    public AddDicomTagPage(AddDicomTagViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    void OnCloseClicked(object? s, EventArgs e)
    {
        (BindingContext as AddDicomTagViewModel)?.Cancel();
        Navigation.PopModalAsync();
    }

    void OnLeftClicked(object? s, EventArgs e)
    {
        var vm = (AddDicomTagViewModel)BindingContext;
        vm.AddLeft();
        Navigation.PopModalAsync();
    }

    void OnRightClicked(object? s, EventArgs e)
    {
        var vm = (AddDicomTagViewModel)BindingContext;
        vm.AddRight();
        Navigation.PopModalAsync();
    }

    void OnBothClicked(object? s, EventArgs e)
    {
        var vm = (AddDicomTagViewModel)BindingContext;
        vm.AddBoth();
        Navigation.PopModalAsync();
    }
}
