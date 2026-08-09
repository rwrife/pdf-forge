using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfForge.Core;
using PdfForge.Core.PdfEngine;

namespace PdfForge.App;

public partial class MainWindow : Window
{
    private readonly PdfCoreEngine _engine = new();
    private readonly ObservableCollection<PageCardViewModel> _pages = [];
    private readonly HashSet<string> _temporaryFilesToDelete = new(StringComparer.OrdinalIgnoreCase);

    private Point _dragStartPoint;
    private PageCardViewModel? _draggedItem;
    private IPdfDocument? _document;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        PageBoard.ItemsSource = _pages;
        SetStatus($"Ready. {CoreHealth.GetVersionBanner()}");
    }

    private async void OpenPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open PDF",
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var appendToExisting = _document is not null;
            await LoadPdfPathsAsync(dialog.FileNames, appendToExisting).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async void MergePdfsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = _document is null ? "Merge PDFs" : "Append PDFs",
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var appendToExisting = _document is not null;
            await LoadPdfPathsAsync(dialog.FileNames, appendToExisting).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            SetStatus("No document loaded. Open a PDF first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save PDF As",
            Filter = "PDF files (*.pdf)|*.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = "pdf-forge-output.pdf"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _document.SaveAsAsync(dialog.FileName).ConfigureAwait(true);
            SetStatus($"Saved current board to '{dialog.FileName}'.");
        }).ConfigureAwait(true);
    }

    private async void ExtractSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            SetStatus("No document loaded. Open a PDF first.");
            return;
        }

        var selectedCards = PageBoard.SelectedItems
            .OfType<PageCardViewModel>()
            .ToList();

        if (selectedCards.Count == 0)
        {
            SetStatus("Select one or more pages to extract.");
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Extract selected pages",
            Filter = "PDF files (*.pdf)|*.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = "pdf-forge-extract.pdf"
        };

        if (saveDialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var selectedSet = new HashSet<PageCardViewModel>(selectedCards);
            var selectedPageNumbers = _pages
                .Where(selectedSet.Contains)
                .Select(card => card.DocumentPageNumber)
                .ToArray();

            await using var extracted = await _engine.ExtractAsync(_document, selectedPageNumbers).ConfigureAwait(true);
            await extracted.SaveAsAsync(saveDialog.FileName).ConfigureAwait(true);

            SetStatus($"Extracted {selectedPageNumbers.Length} page(s) to '{saveDialog.FileName}'.");
        }).ConfigureAwait(true);
    }

    private async void SplitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            SetStatus("No document loaded. Open a PDF first.");
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Choose split output location and file prefix",
            Filter = "PDF files (*.pdf)|*.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = "pdf-forge-split.pdf"
        };

        if (saveDialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var outputDirectory = Path.GetDirectoryName(saveDialog.FileName);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("Could not determine output directory for split files.");
            }

            Directory.CreateDirectory(outputDirectory);
            var outputPrefix = Path.GetFileNameWithoutExtension(saveDialog.FileName);

            var splitDocuments = await _engine.SplitAsync(_document, new PdfSplitMode.OnePerPage()).ConfigureAwait(true);
            try
            {
                for (var index = 0; index < splitDocuments.Count; index++)
                {
                    var outputPath = Path.Combine(outputDirectory, $"{outputPrefix}-page-{index + 1:D3}.pdf");
                    await splitDocuments[index].SaveAsAsync(outputPath).ConfigureAwait(true);
                }
            }
            finally
            {
                foreach (var splitDocument in splitDocuments)
                {
                    await splitDocument.DisposeAsync().ConfigureAwait(true);
                }
            }

            SetStatus($"Split current PDF into {splitDocuments.Count} file(s) in '{outputDirectory}'.");
        }).ConfigureAwait(true);
    }

    private async void CompressButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            SetStatus("No document loaded. Open a PDF first.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var compressed = await _engine.CompressAsync(_document, PdfCompressionSettings.Default).ConfigureAwait(true);
            await ReplaceDocumentAsync(compressed).ConfigureAwait(true);
            await ReloadPageBoardAsync().ConfigureAwait(true);
            SetStatus("Compressed current PDF in memory. Use Save As to write output.");
        }).ConfigureAwait(true);
    }

    private async void RotatePageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            SetStatus("No document loaded. Open a PDF first.");
            return;
        }

        if (sender is not Button button || button.Tag is not PageCardViewModel card)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var rotated = await _engine.RotateAsync(_document, card.DocumentPageNumber, 90).ConfigureAwait(true);
            await ReplaceDocumentAsync(rotated).ConfigureAwait(true);
            await ReloadPageBoardAsync().ConfigureAwait(true);
            SetStatus($"Rotated page {card.DisplayOrder} by +90°.");
        }).ConfigureAwait(true);
    }

    private async void DeletePageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            SetStatus("No document loaded. Open a PDF first.");
            return;
        }

        if (sender is not Button button || button.Tag is not PageCardViewModel card)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var deleted = await _engine.DeleteAsync(_document, card.DocumentPageNumber).ConfigureAwait(true);
            await ReplaceDocumentAsync(deleted).ConfigureAwait(true);
            await ReloadPageBoardAsync().ConfigureAwait(true);
            SetStatus($"Deleted page {card.DisplayOrder}.");
        }).ConfigureAwait(true);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (_isBusy)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var hasPdf = files.Any(path =>
            string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase));

        e.Effects = hasPdf ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_isBusy || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
        var pdfFiles = droppedFiles
            .Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (pdfFiles.Length == 0)
        {
            SetStatus("No PDF files detected in dropped content.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var appendToExisting = _document is not null;
            await LoadPdfPathsAsync(pdfFiles, appendToExisting).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private void PageBoard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _draggedItem = TryGetCardFromSource(e.OriginalSource as DependencyObject);
    }

    private void PageBoard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isBusy || e.LeftButton != MouseButtonState.Pressed || _draggedItem is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        var delta = position - _dragStartPoint;

        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(PageBoard, _draggedItem, DragDropEffects.Move);
        _draggedItem = null;
    }

    private async void PageBoard_Drop(object sender, DragEventArgs e)
    {
        if (_isBusy || _document is null || !e.Data.GetDataPresent(typeof(PageCardViewModel)))
        {
            return;
        }

        var draggedCard = e.Data.GetData(typeof(PageCardViewModel)) as PageCardViewModel;
        var targetCard = TryGetCardFromSource(e.OriginalSource as DependencyObject);

        if (draggedCard is null || targetCard is null || ReferenceEquals(draggedCard, targetCard))
        {
            return;
        }

        var sourceIndex = _pages.IndexOf(draggedCard);
        var targetIndex = _pages.IndexOf(targetCard);

        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            _pages.Move(sourceIndex, targetIndex);
            UpdateDisplayOrder();

            var reorderedDocument = await _engine.ReorderAsync(
                    _document,
                    _pages.Select(page => page.DocumentPageNumber).ToArray())
                .ConfigureAwait(true);

            await ReplaceDocumentAsync(reorderedDocument).ConfigureAwait(true);

            for (var index = 0; index < _pages.Count; index++)
            {
                _pages[index].DocumentPageNumber = index + 1;
            }

            UpdateDisplayOrder();
            SetStatus("Reordered pages in board and core document state.");
        }).ConfigureAwait(true);
    }

    private async Task LoadPdfPathsAsync(IReadOnlyList<string> pdfPaths, bool appendToExisting)
    {
        if (pdfPaths.Count == 0)
        {
            return;
        }

        var normalizedPdfPaths = pdfPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToArray();

        if (normalizedPdfPaths.Length == 0)
        {
            return;
        }

        IPdfDocument nextDocument;

        if (appendToExisting && _document is not null)
        {
            var tempCurrent = await SaveCurrentDocumentToTempFileAsync().ConfigureAwait(true);
            try
            {
                var sourcePaths = new List<string> { tempCurrent };
                sourcePaths.AddRange(normalizedPdfPaths);
                nextDocument = await _engine.MergeAsync(sourcePaths).ConfigureAwait(true);
            }
            finally
            {
                DeleteFileIfExists(tempCurrent);
            }

            SetStatus($"Appended {normalizedPdfPaths.Length} PDF(s). Rendering board...");
        }
        else if (normalizedPdfPaths.Length == 1)
        {
            nextDocument = await _engine.OpenAsync(normalizedPdfPaths[0]).ConfigureAwait(true);
            SetStatus("Opened PDF. Rendering board...");
        }
        else
        {
            nextDocument = await _engine.MergeAsync(normalizedPdfPaths).ConfigureAwait(true);
            SetStatus($"Merged {normalizedPdfPaths.Length} PDFs. Rendering board...");
        }

        await ReplaceDocumentAsync(nextDocument).ConfigureAwait(true);
        await ReloadPageBoardAsync().ConfigureAwait(true);
        SetStatus($"Loaded {_pages.Count} page(s). Drag cards to reorder, then Save As.");
    }

    private async Task ReloadPageBoardAsync()
    {
        _pages.Clear();

        if (_document is null)
        {
            return;
        }

        for (var pageNumber = 1; pageNumber <= _document.PageCount; pageNumber++)
        {
            var renderResult = await _engine.RenderPageAsync(
                    _document,
                    pageNumber,
                    targetWidth: 160,
                    targetHeight: 220)
                .ConfigureAwait(true);

            _pages.Add(new PageCardViewModel(
                documentPageNumber: pageNumber,
                displayOrder: pageNumber,
                thumbnail: ToBitmapImage(renderResult.ImageBytes)));
        }

        UpdateDisplayOrder();
    }

    private async Task ReplaceDocumentAsync(IPdfDocument nextDocument)
    {
        var previousDocument = _document;
        _document = nextDocument;

        if (previousDocument is not null)
        {
            await previousDocument.DisposeAsync().ConfigureAwait(true);
        }
    }

    private async Task<string> SaveCurrentDocumentToTempFileAsync()
    {
        if (_document is null)
        {
            throw new InvalidOperationException("No document is loaded.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-current-{Guid.NewGuid():N}.pdf");
        await _document.SaveAsAsync(tempPath).ConfigureAwait(true);

        _temporaryFilesToDelete.Add(tempPath);
        return tempPath;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus($"Operation failed: {ex.Message}");
            MessageBox.Show(
                this,
                ex.Message,
                "pdf-forge",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private static BitmapImage ToBitmapImage(byte[] imageBytes)
    {
        using var memoryStream = new MemoryStream(imageBytes);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = memoryStream;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    private void UpdateDisplayOrder()
    {
        for (var index = 0; index < _pages.Count; index++)
        {
            _pages[index].DisplayOrder = index + 1;
        }
    }

    private static PageCardViewModel? TryGetCardFromSource(DependencyObject? source)
    {
        if (source is null)
        {
            return null;
        }

        var listBoxItem = FindVisualParent<ListBoxItem>(source);
        return listBoxItem?.DataContext as PageCardViewModel;
    }

    private static T? FindVisualParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        var parent = child;
        while (parent is not null)
        {
            if (parent is T typed)
            {
                return typed;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (_document is not null)
        {
            await _document.DisposeAsync().ConfigureAwait(true);
            _document = null;
        }

        foreach (var tempPath in _temporaryFilesToDelete)
        {
            DeleteFileIfExists(tempPath);
        }

        _temporaryFilesToDelete.Clear();
        base.OnClosed(e);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort cleanup only
        }
    }
}

public sealed class PageCardViewModel : INotifyPropertyChanged
{
    private int _displayOrder;
    private ImageSource _thumbnail;

    public PageCardViewModel(int documentPageNumber, int displayOrder, ImageSource thumbnail)
    {
        DocumentPageNumber = documentPageNumber;
        _displayOrder = displayOrder;
        _thumbnail = thumbnail;
    }

    public int DocumentPageNumber { get; set; }

    public int DisplayOrder
    {
        get => _displayOrder;
        set
        {
            if (_displayOrder == value)
            {
                return;
            }

            _displayOrder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string DisplayTitle => $"Page {DisplayOrder}";

    public ImageSource Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
