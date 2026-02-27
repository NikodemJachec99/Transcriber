using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Storage;

public interface ITranscriptFileWriter
{
    Task<TranscriptFiles> WriteAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);
}
