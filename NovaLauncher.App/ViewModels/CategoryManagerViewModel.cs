using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.Services;

namespace NovaLauncher.App.ViewModels;

public sealed record CategoryViewModel(Guid Id, string CategoryName)
    : NavigationItem(CategoryName, CategoryName == "游戏" ? "\uE7FC" : CategoryName == "应用" ? "\uE71D" : "\uE8B7", Id);
public sealed record CategoryChoice(Guid? Id, string Name);

public sealed class CategoryManagerViewModel : ViewModelBase
{
    private readonly ICategoryService? _service;
    private readonly IToastService _toast;
    private bool _isEditing;
    private bool _isReady;
    private string _newName = "";
    private string? _error;
    public ObservableCollection<CategoryViewModel> Categories { get; } = [];
    public ObservableCollection<CategoryChoice> Choices { get; } = [new(null, "未分类")];
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }
    public bool IsReady { get => _isReady; private set { SetProperty(ref _isReady, value); CreateCommand.NotifyCanExecuteChanged(); } }
    public string NewName { get => _newName; set { SetProperty(ref _newName, value); Error = null; CreateCommand.NotifyCanExecuteChanged(); } }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public IRelayCommand BeginCreateCommand { get; }
    public IRelayCommand CancelCreateCommand { get; }
    public IAsyncRelayCommand CreateCommand { get; }

    public CategoryManagerViewModel(ICategoryService? service, IToastService toast)
    {
        _service = service;
        _toast = toast;
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => IsReady && CategoryService.ValidateName(NewName) is null);
        BeginCreateCommand = new RelayCommand(() => { if (!CreateCommand.IsRunning) { NewName = ""; Error = null; IsEditing = true; } });
        CancelCreateCommand = new RelayCommand(() => { if (!CreateCommand.IsRunning) { IsEditing = false; NewName = ""; } });
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        if (_service is null) return;
        try
        {
            foreach (var item in await _service.GetAllAsync(token)) Add(item.Id, item.Name);
            IsReady = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Trace.TraceError("Categories load failed: {0}", ex); _toast.Show("分类加载失败"); }
    }

    private void Add(Guid id, string name)
    {
        if (Categories.Any(x => x.Id == id)) return;
        Categories.Add(new(id, name));
        Choices.Add(new(id, name));
    }

    private async Task CreateAsync()
    {
        if (_service is null || !IsReady) return;
        try
        {
            var result = await _service.CreateAsync(NewName, CancellationToken.None);
            if (result.Category is { } category)
            {
                Add(category.Id, category.Name);
                IsEditing = false;
                NewName = "";
            }
            else { Error = result.Error ?? "分类保存失败"; _toast.Show(Error); }
        }
        catch (Exception ex) { Trace.TraceError("Category create failed: {0}", ex); Error = "分类保存失败"; _toast.Show(Error); }
    }
}
