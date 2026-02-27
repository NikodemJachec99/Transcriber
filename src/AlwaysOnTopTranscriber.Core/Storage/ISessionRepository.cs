using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Storage;

public interface ISessionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<long> AddSessionAsync(SessionEntity session, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionEntity>> GetSessionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionEntity>> SearchSessionsAsync(string query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionEntity>> QuerySessionsAsync(SessionQueryOptions options, CancellationToken cancellationToken);

    Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken);

    Task RenameSessionAsync(long sessionId, string newName, CancellationToken cancellationToken);

    Task UpdateSessionNotesAsync(long sessionId, string notes, CancellationToken cancellationToken);

    Task UpdateSessionTagsAsync(long sessionId, string tags, CancellationToken cancellationToken);

    Task UpdateSessionTranscriptAsync(long sessionId, string transcriptText, int wordCount, CancellationToken cancellationToken);

    Task UpdateSessionTextPathAsync(long sessionId, string textPath, CancellationToken cancellationToken);
}
