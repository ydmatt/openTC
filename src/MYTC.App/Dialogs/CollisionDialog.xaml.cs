using System.Windows;
using MYTC.Domain.Operations;

namespace MYTC.App.Dialogs;

public partial class CollisionDialog
{
    public CollisionDialog()
    {
        InitializeComponent();
    }

    public CollisionBehavior? SelectedBehavior { get; private set; }

    private void OnKeepBothClick(object sender, RoutedEventArgs e)
    {
        Complete(CollisionBehavior.KeepBoth);
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        Complete(CollisionBehavior.Skip);
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        Complete(CollisionBehavior.Replace);
    }

    private void Complete(CollisionBehavior behavior)
    {
        SelectedBehavior = behavior;
        DialogResult = true;
    }
}
