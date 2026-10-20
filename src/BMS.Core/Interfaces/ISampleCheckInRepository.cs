using BMS.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Core.Interfaces
{
    public interface ISampleRepository
    {
        Task<Sample?> FindByCodeAsync(string code, CancellationToken ct = default);
        Task CreateAsync(Sample sample, CancellationToken ct = default);
        Task UpdateAsync(Sample sample, CancellationToken ct = default);
        Task<List<Sample>> ListPendingCheckInAsync(CancellationToken ct = default);
    }
}
