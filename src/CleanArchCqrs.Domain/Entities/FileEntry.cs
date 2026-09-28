using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class FileEntry : BaseEntity
    {
        public Guid TenantId { get; private set; }

        public Guid OwnerId { get; private set; }

        public Guid? FolderId { get; private set; }

        public string Name { get; private set; } = string.Empty;

        public string StorageKey { get; private set; } = string.Empty;

        public string ContentType { get; private set; } = string.Empty;

        public long SizeBytes { get; private set; }

        public string? Hash { get; private set; }

        public bool IsDeleted { get; private set; }

        public Tenant Tenant { get; private set; } = null!;

        public User Owner { get; private set; } = null!;

        public Folder? Folder { get; private set; }

        public ICollection<FileShare> Shares { get; private set; }
            = new List<FileShare>();
    }
}
