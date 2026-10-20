using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data access interface for phlebotomy sample collection lists (UC37).
/// </summary>
public interface ISampleListRepository
{
    Task<SampleCollectionList?> GetByIdAsync(string id);
    Task<SampleCollectionList?> GetByBatchCodeAsync(string batchCode);
    Task<IEnumerable<SampleCollectionList>> GetAllAsync(DateTime? date = null, SampleCollectionListStatus? status = null);
    Task<SampleCollectionList> CreateAsync(SampleCollectionList list);
    Task<bool> UpdateAsync(SampleCollectionList list);
    Task<string> GenerateNextBatchCodeAsync();
}
