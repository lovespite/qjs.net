using QuickJsNet.Core;
using System.Text.Json.Serialization;

namespace QuickJsNet.Modules;

internal static class WindowsSimple
{
    public static void Alert(string? message)
    {
        //System.Windows.Forms.MessageBox.Show(message);
    }

    public static bool Confirm(string? message)
    {
        //var ret = System.Windows.Forms.MessageBox.Show(message, "Confirm", System.Windows.Forms.MessageBoxButtons.OKCancel);
        //return ret == System.Windows.Forms.DialogResult.OK;
        return false;
    }

    public static string? Prompt(string? message, string? defaultValue = "", string okButtonText = "OK", string cancelButtonText = "Cancel")
    {
        //var prompt = new PromptDialog(message, defaultValue, okButtonText, cancelButtonText);
        //return prompt.ShowDialog() == System.Windows.Forms.DialogResult.OK ? prompt.Value : null;
        return defaultValue;
    }

    public static string? Select(SelectOption[] options, string? defaultValue = "", string title = "Select", string message = "Please select an option:")
    {
        //var select = new SelectDialog(options, defaultValue, title, message);
        //return select.ShowDialog() == System.Windows.Forms.DialogResult.OK ? select.Value : null;
        return defaultValue;
    }

    internal class SelectOption
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
        [JsonPropertyName("title")]
        public string? Display { get; set; }
        public override string ToString() => Display ?? "";
    }

    //internal class PromptDialog : System.Windows.Forms.Form
    //{
    //    private readonly System.Windows.Forms.TextBox _textBox;
    //    private readonly System.Windows.Forms.Button _okButton;
    //    private readonly System.Windows.Forms.Button _cancelButton;
    //    public string Value => _textBox.Text;
    //    public PromptDialog(string message, string defaultValue, string okButtonText = "OK", string cancelButtonText = "Cancel")
    //    {
    //        Text = "Input";
    //        Width = 400;
    //        Height = 150;
    //        var label = new System.Windows.Forms.Label { Text = message, Left = 10, Top = 10, Width = 360 };
    //        _textBox = new System.Windows.Forms.TextBox { Left = 10, Top = 40, Width = 360, Text = defaultValue };
    //        _okButton = new System.Windows.Forms.Button { Text = okButtonText, Left = 220, Top = 70, DialogResult = System.Windows.Forms.DialogResult.OK };
    //        _cancelButton = new System.Windows.Forms.Button { Text = cancelButtonText, Left = 300, Top = 70, DialogResult = System.Windows.Forms.DialogResult.Cancel };
    //        Controls.Add(label);
    //        Controls.Add(_textBox);
    //        Controls.Add(_okButton);
    //        Controls.Add(_cancelButton);
    //        AcceptButton = _okButton;
    //        CancelButton = _cancelButton;
    //        _textBox.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
    //        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
    //    }
    //}

    //internal class SelectDialog : System.Windows.Forms.Form
    //{
    //    private readonly System.Windows.Forms.ComboBox _comboBox;
    //    private readonly System.Windows.Forms.Button _okButton;
    //    private readonly System.Windows.Forms.Button _cancelButton;
    //    public string Value => (_comboBox.SelectedItem as SelectOption)?.Value;
    //    public SelectDialog(SelectOption[] options, string defaultValue, string title = "Select", string message = "Please select an option:")
    //    {
    //        Text = title;
    //        Width = 400;
    //        Height = 150;
    //        var label = new System.Windows.Forms.Label { Text = message, Left = 10, Top = 10, Width = 360 };
    //        _comboBox = new System.Windows.Forms.ComboBox { Left = 10, Top = 40, Width = 360, DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList };
    //        _comboBox.Items.AddRange(options);
    //        if (options.Any(o => o.Value == defaultValue))
    //            _comboBox.SelectedItem = options.First(o => o.Value == defaultValue);
    //        _okButton = new System.Windows.Forms.Button { Text = "OK", Left = 220, Top = 70, DialogResult = System.Windows.Forms.DialogResult.OK };
    //        _cancelButton = new System.Windows.Forms.Button { Text = "Cancel", Left = 300, Top = 70, DialogResult = System.Windows.Forms.DialogResult.Cancel };
    //        Controls.Add(label);
    //        Controls.Add(_comboBox);
    //        Controls.Add(_okButton);
    //        Controls.Add(_cancelButton);
    //        AcceptButton = _okButton;
    //        CancelButton = _cancelButton;
    //        _comboBox.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
    //        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
    //    }
    //}

    public static void Install(QuickJSRuntime engine)
    {
        engine.RegisterGlobalFunction("alert", (args =>
        {
            if (args.Length == 0) return null;
            var message = engine.GetString(args[0]);
            Alert(message);
            return null;
        }));

        engine.RegisterGlobalFunction("confirm", (args =>
        {
            if (args.Length == 0) return null;
            var message = engine.GetString(args[0]);
            return Confirm(message);
        }));

        engine.RegisterGlobalFunction("prompt", (args =>
        {
            if (args.Length == 0) return null;
            var message = engine.GetString(args[0]);
            var defaultValue = args.Length > 1 ? engine.GetString(args[1]) : "";
            return Prompt(message, defaultValue);
        }));

        engine.RegisterGlobalFunction("select", (args =>
        {
            if (args.Length == 0) return null;
            var options = ParseSelectOptions(engine.JSValueToManaged(args[0]) as string);
            if (options is null || options.Length == 0) return null;
            var defaultValue = args.Length > 1 ? engine.GetString(args[1]) : "";
            return Select(options, defaultValue);
        }));
    }

    static SelectOption[]? ParseSelectOptions(string? raw)
    {
        if (raw is null) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(raw, QuickJsNet.Utils.QuickJsJsonContext.Default.SelectOptionArray);
        }
        catch
        {
        }
        return raw.Split('|').Select(o => new SelectOption { Value = o, Display = o }).ToArray();
    }
}
