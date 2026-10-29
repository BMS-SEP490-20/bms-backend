using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;

namespace BMS.Api.Controllers.Laboratory
{
    /// <summary>
    /// API Controller for managing laboratory rooms within the hospital.
    /// Supports room listing, retrieval, creation, updating, and deletion.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
    public class LabRoomController : ControllerBase
    {
        private readonly ILabRoomService _service;

        /// <summary>
        /// Initializes a new instance of the <see cref="LabRoomController"/> class.
        /// </summary>
        /// <param name="service">The laboratory room business service.</param>
        public LabRoomController(ILabRoomService service)
        {
            _service = service;
        }

        /// <summary>
        /// Retrieves a paginated list of all laboratory rooms in the system.
        /// Feature Task: View Lab Rooms (Backend).
        /// </summary>
        /// <param name="page">The current page index (1-based). Default is 1.</param>
        /// <param name="pageSize">The number of records per page. Default is 10.</param>
        /// <returns>A paginated response object containing room records and metadata.</returns>
        /// <response code="200">Returns the paginated lab room list.</response>
        /// <response code="400">If page or pageSize is less than or equal to 0.</response>
        /// <response code="404">If no laboratory rooms match the criteria.</response>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            if (page <= 0 || pageSize <= 0)
                return BadRequest(new { message = "Giá trị phân trang không hợp lệ." });

            // Paging is done in MongoDB (Skip/Limit) instead of loading every room into memory.
            var (pagedRooms, totalCount) = await _service.GetPagedAsync(page, Math.Min(pageSize, 100), ct);

            if (pagedRooms.Count == 0)
                return NotFound(new { message = "Không có phòng nào được tìm thấy." });

            return Ok(new
            {
                currentPage = page,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                data = pagedRooms
            });
        }

        /// <summary>
        /// Retrieves a single laboratory room by its unique identifier.
        /// Feature Task: View Lab Rooms (Backend).
        /// </summary>
        /// <param name="id">The unique identifier of the laboratory room.</param>
        /// <returns>The detailed laboratory room object.</returns>
        /// <response code="200">Returns the room details.</response>
        /// <response code="404">If the specified lab room is not found.</response>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            var room = await _service.GetByIdAsync(id, ct);
            if (room == null)
                return NotFound(new { message = "Không tìm thấy phòng." });

            return Ok(room);
        }

        /// <summary>
        /// Creates a new laboratory room in the system.
        /// Feature Task: Create Lab Rooms (Backend).
        /// </summary>
        /// <param name="dto">The creation payload containing room number, name, department, capacity, and status.</param>
        /// <returns>A confirmation response on successful creation.</returns>
        /// <response code="200">If the room was created successfully.</response>
        /// <response code="400">If the provided model state is invalid.</response>
        /// <response code="500">If an unhandled server error occurs (standard error body, no internal details).</response>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateLabRoomDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _service.CreateAsync(dto, ct);
            return Ok(new { message = "Tạo phòng thành công.", data = created });
        }

        /// <summary>
        /// Updates an existing laboratory room's attributes.
        /// Feature Task: Update Lab Rooms (Backend).
        /// </summary>
        /// <param name="id">The unique identifier of the room to update.</param>
        /// <param name="dto">The update payload containing modified room attributes.</param>
        /// <returns>A confirmation response on successful update.</returns>
        /// <response code="200">If the room was updated successfully.</response>
        /// <response code="404">If the target room cannot be found.</response>
        /// <response code="500">If an unhandled server error occurs (standard error body, no internal details).</response>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateLabRoomDto dto, CancellationToken ct)
        {
            // Not-found is raised as ResourceNotFoundException and mapped to 404 by GlobalExceptionHandler.
            await _service.UpdateAsync(id, dto, ct);
            return Ok(new { message = "Cập nhật phòng thành công." });
        }

        /// <summary>
        /// Deletes a laboratory room from the database.
        /// Feature Task: Delete Lab Rooms (Backend).
        /// </summary>
        /// <param name="id">The unique identifier of the room to delete.</param>
        /// <returns>A confirmation response on successful deletion.</returns>
        /// <response code="200">If the room was deleted successfully.</response>
        /// <response code="404">If the room to delete was not found.</response>
        /// <response code="500">If an unhandled server error occurs (standard error body, no internal details).</response>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            // Soft delete: the room stays referenced by historical shifts and lab profiles.
            await _service.DeleteAsync(id, ct);
            return Ok(new { message = "Xóa phòng thành công." });
        }
    }
}
