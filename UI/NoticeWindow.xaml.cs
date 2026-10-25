using System.Windows;
using GBooster.Core;

namespace GBooster.UI
{
    public partial class NoticeWindow : Window
    {
        public NoticeWindow()
        {
            InitializeComponent();
            Loc.Apply(this);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
