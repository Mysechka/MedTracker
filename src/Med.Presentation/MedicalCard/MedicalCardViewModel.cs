using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;

namespace Med.Presentation.MedicalCard;

public sealed partial class MedicalCardViewModel : ViewModelBase
{
    private readonly IDiagnosisRepository _diagnoses;
    private readonly IDocumentRepository _documents;
    private readonly IFileStorage _storage;
    private readonly IAuthService _auth;
    private readonly UploadDocumentUseCase _upload;

    public MedicalCardViewModel(
        IDiagnosisRepository diagnoses,
        IDocumentRepository documents,
        IFileStorage storage,
        IAuthService auth,
        UploadDocumentUseCase upload)
    {
        _diagnoses = diagnoses;
        _documents = documents;
        _storage = storage;
        _auth = auth;
        _upload = upload;
    }

    public ObservableCollection<Diagnosis> Diagnoses { get; } = [];

    public ObservableCollection<Document> Documents { get; } = [];

    [ObservableProperty]
    private Diagnosis? _selectedDiagnosis;

    [ObservableProperty]
    private Document? _selectedDocument;

    [ObservableProperty]
    private bool _showDevelopmentNotice = true;

    [ObservableProperty]
    private string _diagnosisTitle = string.Empty;

    [ObservableProperty]
    private string _doctor = string.Empty;

    [ObservableProperty]
    private string _fileName = "note.txt";

    [ObservableProperty]
    private string _fileContent = "demo";

    [ObservableProperty]
    private string _mimeType = "text/plain";

    [ObservableProperty]
    private string _docType = "LabResult";

    [ObservableProperty]
    private string _signedUrl = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Diagnoses.Clear();
            Documents.Clear();
            foreach (Diagnosis diagnosis in await _diagnoses.ListAsync(cancellationToken))
            {
                Diagnoses.Add(diagnosis);
            }

            foreach (Document document in await _documents.ListAsync(cancellationToken))
            {
                Documents.Add(document);
            }

            Message = $"Диагнозов: {Diagnoses.Count}; документов: {Documents.Count}";
        });
    }

    [RelayCommand]
    private async Task SaveDiagnosisAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId
                ?? throw new InvalidOperationException("Нужна сессия.");

            Diagnosis diagnosis = Diagnosis.Create(
                SelectedDiagnosis?.Id ?? Guid.NewGuid(),
                userId,
                DiagnosisTitle,
                string.IsNullOrWhiteSpace(Doctor) ? null : Doctor);

            await _diagnoses.UpsertAsync(diagnosis, cancellationToken);
            SelectedDiagnosis = diagnosis;
            await RefreshAsync(cancellationToken);
            Message = "Диагноз сохранён.";
        });
    }

    [RelayCommand]
    private async Task UploadAsync(CancellationToken cancellationToken)
    {
        if (SelectedDiagnosis is null)
        {
            Message = "Выберите диагноз для привязки документа.";
            return;
        }

        if (!Enum.TryParse(DocType, ignoreCase: true, out DocumentType docType))
        {
            Message = "Некорректный DocType.";
            return;
        }

        await RunAsync(async () =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(FileContent);
            Document document = await _upload.ExecuteAsync(
                FileName,
                bytes,
                MimeType,
                docType,
                SelectedDiagnosis.Id,
                courseId: null,
                cancellationToken);
            Message = $"Загружено: {document.StoragePath}";
            await RefreshAsync(cancellationToken);
        });
    }

    [RelayCommand]
    private async Task OpenSignedUrlAsync(CancellationToken cancellationToken)
    {
        if (SelectedDocument is null)
        {
            Message = "Выберите документ.";
            return;
        }

        await RunAsync(async () =>
        {
            Uri url = await _storage.CreateSignedUrlAsync(SelectedDocument.StoragePath, cancellationToken);
            SignedUrl = url.ToString();
            Message = "Signed URL создан.";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
