using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZapretDPI.Services;
using ZapretDPI.Views;

namespace ZapretDPI;

public partial class ProfileSaveWindow : Window
{
    public string? SelectedStrategy { get; private set; }
    public string? ProfileName { get; private set; }
    public bool ApplyImmediately => ChkApplyImmediately.IsChecked == true;

    public ProfileSaveWindow(List<string> strategies)
    {
        InitializeComponent();

        Title = LocalizationService.Get("ProfileSave_Title");
        LblTitle.Text = LocalizationService.Get("ProfileSave_Header");
        LblDesc.Text = LocalizationService.Get("ProfileSave_Desc");
        LblName.Text = LocalizationService.Get("ProfileSave_NameLabel");
        ChkApplyImmediately.Content = LocalizationService.Get("ProfileSave_ApplyNow");
        BtnCancel.Content = LocalizationService.Get("Btn_Cancel");
        BtnSave.Content = LocalizationService.Get("Btn_SaveAndApply");

        TxtProfileName.Text = $"Discord & Web ({DateTime.Now:dd.MM.yyyy HH:mm})";

        LstStrategies.ItemsSource = strategies;
        if (strategies.Count > 0)
        {
            LstStrategies.SelectedIndex = 0;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnCopyStrategy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string strat)
        {
            Clipboard.SetText(strat);
            btn.Content = "✓ Kopyalandı";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (LstStrategies.SelectedItem == null)
        {
            DarkMessageBox.Show(LocalizationService.Get("Msg_SelectStrategy"), LocalizationService.Get("Dialog_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtProfileName.Text))
        {
            DarkMessageBox.Show(LocalizationService.Get("Msg_EnterProfileName"), LocalizationService.Get("Dialog_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedStrategy = (string)LstStrategies.SelectedItem;
        ProfileName = TxtProfileName.Text.Trim();
        DialogResult = true;
        Close();
    }
}
