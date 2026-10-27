using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TestOrder.Models.DTO;
using TestOrder.Services.Interface;
using Testorder_service.Models.DTO;
using Testorder_service.Services.Interface;

namespace TestOrder.Controllers
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
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (page <= 0 || pageSize <= 0)
                return BadRequest(new { message = "Giá trị phân trang không hợp lệ." });

            var allRooms = await _service.GetAllAsync();
            var totalCount = allRooms.Count();

            var pagedRooms = allRooms
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            if (!pagedRooms.Any())
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
        public async Task<IActionResult> GetById(string id)
        {
            var room = await _service.GetByIdAsync(id);
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
        /// <response code="500">If an unhandled server error occurs.</response>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateLabRoomDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await _service.CreateAsync(dto);
                return Ok(new { message = "Tạo phòng thành công." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi tạo phòng.", error = ex.Message });
            }
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
        /// <response code="500">If an unhandled server error occurs.</response>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateLabRoomDto dto)
        {
            try
            {
                await _service.UpdateAsync(id, dto);
                return Ok(new { message = "Cập nhật phòng thành công." });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Không tìm thấy phòng để cập nhật." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi cập nhật phòng.", error = ex.Message });
            }
        }

        /// <summary>
        /// Deletes a laboratory room from the database.
        /// Feature Task: Delete Lab Rooms (Backend).
        /// </summary>
        /// <param name="id">The unique identifier of the room to delete.</param>
        /// <returns>A confirmation response on successful deletion.</returns>
        /// <response code="200">If the room was deleted successfully.</response>
        /// <response code="404">If the room to delete was not found.</response>
        /// <response code="500">If an unhandled server error occurs.</response>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _service.DeleteAsync(id);
                return Ok(new { message = "Xóa phòng thành công." });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Không tìm thấy phòng để xóa." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi xóa phòng.", error = ex.Message });
            }
        }
    }
}
