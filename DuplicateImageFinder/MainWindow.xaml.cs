using System.Windows.Input;
using ImageCollectionTool.ViewModels;

namespace ImageCollectionTool
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        private void commonWordsTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return && DataContext is MainViewModel vm && vm.RunCommand.CanExecute(null))
                vm.RunCommand.Execute(null);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.Cleanup();
            base.OnClosing(e);
        }
    }
}
