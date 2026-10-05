using System.Windows;
using System.Windows.Input;

namespace MicRec.Dialogs;

public partial class PromptDialog : Window
{
    public string ResultText { get; private set; } = "";

    public PromptDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Accept();
        else if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    private void Accept()
    {
        ResultText = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(ResultText))
            return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
