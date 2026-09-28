using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class FileShare : BaseEntity
    {
        public Guid FileEntryId { get; private set; }

        public Guid SharedWithUserId { get; private set; }

        public bool CanRead { get; private set; }

        public bool CanWrite { get; private set; }

        public bool CanDelete { get; private set; }

        public DateTime? ExpiresAt { get; private set; }

        public FileEntry FileEntry { get; private set; } = null!;

        public User SharedWithUser { get; private set; } = null!;
    }
}
