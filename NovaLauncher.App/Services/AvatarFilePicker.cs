using Microsoft.Win32;

namespace NovaLauncher.App.Services;

public interface IAvatarFilePicker { string? Pick(); }
public sealed class AvatarFilePicker : IAvatarFilePicker
{
    public string? Pick()
    {
        var picker = new OpenFileDialog { Title = "更换头像", Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true, Multiselect = false };
        return picker.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? picker.FileName : null;
    }
}
