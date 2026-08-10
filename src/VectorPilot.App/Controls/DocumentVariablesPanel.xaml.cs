using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

public partial class DocumentVariablesPanel : UserControl
{
    private const string InvalidExpression = "invalid expression";

    private readonly DocumentVariablesViewModel _vm;

    public DocumentVariablesPanel() : this(DefaultFilePath()) { }

    public DocumentVariablesPanel(string filePath)
    {
        _vm = new DocumentVariablesViewModel(filePath);
        InitializeComponent();
        Loaded += (_, _) => RefreshAll();
    }

    private static string DefaultFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "VectorPilot", "documentVariables.json");

    private void RefreshAll()
    {
        RefreshVariables();
        RefreshDimensions();
    }

    private void RefreshVariables()
    {
        var selected = LstVariables.SelectedItem;
        LstVariables.ItemsSource = null;
        LstVariables.ItemsSource = _vm.Variables;
        if (selected is DocumentVariable v) LstVariables.SelectedItem = v;
        else if (_vm.Variables.Count > 0) LstVariables.SelectedIndex = 0;
    }

    private void LstVariables_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstVariables.SelectedItem is DocumentVariable v)
        {
            TxtKey.Text = v.Key;
            TxtValue.Text = v.Value;
            TxtCategory.Text = v.Category;
        }
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var v = _vm.AddVariable(
            TxtKey.Text.Trim(),
            TxtValue.Text.Trim(),
            string.IsNullOrWhiteSpace(TxtCategory.Text) ? "General" : TxtCategory.Text.Trim());
        _vm.Save();
        RefreshVariables();
        LstVariables.SelectedItem = v;
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (LstVariables.SelectedIndex < 0) return;
        _vm.DeleteVariable(LstVariables.SelectedIndex);
        _vm.Save();
        RefreshVariables();
    }

    private void EditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (LstVariables.SelectedItem is not DocumentVariable v) return;
        v.Key = TxtKey.Text.Trim();
        v.Value = TxtValue.Text.Trim();
        v.Category = string.IsNullOrWhiteSpace(TxtCategory.Text) ? "General" : TxtCategory.Text.Trim();
        _vm.Save();
        RefreshVariables();
    }

    private void RefreshDimensions()
    {
        LstDimensions.Items.Clear();
        foreach (var dim in _vm.Dimensions)
            LstDimensions.Items.Add(BuildDimensionRow(dim));
    }

    private Grid BuildDimensionRow(DrivenDimension dim)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyBox = new TextBox { Text = dim.Key, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        keyBox.TextChanged += (_, _) => dim.Key = keyBox.Text;

        var exprBox = new TextBox { Text = dim.Expression, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var preview = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        exprBox.TextChanged += (_, _) =>
        {
            dim.Expression = exprBox.Text;
            UpdatePreview(dim, preview);
        };

        Grid.SetColumn(keyBox, 0);
        Grid.SetColumn(exprBox, 1);
        Grid.SetColumn(preview, 2);
        row.Children.Add(keyBox);
        row.Children.Add(exprBox);
        row.Children.Add(preview);

        UpdatePreview(dim, preview);
        return row;
    }

    private void UpdatePreview(DrivenDimension dim, TextBlock preview)
    {
        var text = _vm.PreviewExpression(dim.Expression);
        if (text == InvalidExpression)
        {
            preview.Text = InvalidExpression;
            preview.Foreground = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)); // red
        }
        else
        {
            preview.Text = text;
            preview.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x44)); // green
        }
    }
}
