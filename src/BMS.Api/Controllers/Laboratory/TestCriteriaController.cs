using System.Text.Json.Serialization;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

[ApiController]
[Route("api/test-criteria")]
[Produces("application/json")]
public sealed class TestCriteriaController(ITestCriterionRepository repo) : ControllerBase
{
    /// <summary>
    /// Lấy danh sách xét nghiệm từ collection test_criteria để chọn khi tạo gói dịch vụ.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<TestCriterionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TestCriterionDto>>> GetAll(CancellationToken ct)
    {
        var items = await repo.GetAllAsync(ct);
        return Ok(items.Select(x => new TestCriterionDto(x.Id, x.Code, x.Name, x.Unit, x.Category)));
    }
}

public sealed record TestCriterionDto(
    [property: JsonPropertyName("_id")] string Id,
    string Code,
    string Name,
    string Unit,
    string Category
);