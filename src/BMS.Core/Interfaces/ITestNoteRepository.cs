using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for laboratory test notes (UC52 / Add Test Notes).
/// </summary>
public interface ITestNoteRepository
{
    Task<TestNote> CreateAsync(TestNote note);
    Task<TestNote?> GetByIdAsync(string id);
    Task<List<TestNote>> GetBySpecimenAsync(string specimenIdOrBarcode);
    Task<List<TestNote>> GetAllAsync();
}
