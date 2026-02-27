using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Utilities;

public interface IModelManager
{
    Task<IReadOnlyList<ModelDescriptor>> GetAvailableAsync(CancellationToken cancellationToken);

    Task<string> DownloadAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);
}
