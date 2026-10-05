using System.Linq;
using System.Windows;
using MicRec.Library;

namespace MicRec.Dialogs;

public partial class ConvertDialog : Window
{
    private sealed record FormatOption(ConvertFormat Format, string Label);

    public ConvertFormat SelectedFormat { get; private set; } = ConvertFormat.Mp3_192;

    public ConvertDialog(string sourceFileName)
    {
        InitializeComponent();
        SourceText.Text = $"Převést „{sourceFileName}“ na:";

        var options = AudioConverter.AllFormats
            .Select(f => new FormatOption(f, AudioConverter.GetLabel(f)))
            .ToList();

        FormatCombo.ItemsSource = options;
        FormatCombo.DisplayMemberPath = nameof(FormatOption.Label);
        FormatCombo.SelectedIndex = options.FindIndex(o => o.Format == ConvertFormat.Mp3_192);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (FormatCombo.SelectedItem is FormatOption option)
            SelectedFormat = option.Format;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
