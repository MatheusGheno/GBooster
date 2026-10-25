using System.Windows;
using GBooster.Core;

namespace GBooster.UI
{
    /// <summary>Janelinha de uma pergunta com um campo de texto (nome do jogo).</summary>
    public partial class PromptWindow : Window
    {
        public string Value => Input.Text.Trim();

        public PromptWindow(string question, string initial)
        {
            InitializeComponent();
            Loc.Apply(this);
            Question.Text = Loc.T(question);
            Input.Text = initial;
            Loaded += (s, e) => { Input.Focus(); Input.SelectAll(); };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (Value.Length > 0)
            {
                DialogResult = true;
            }
        }
    }
}
