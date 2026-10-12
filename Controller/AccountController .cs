using iam_service.Modals.Dto;
using iam_service.Service.Interface;
using IamService.Services.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace IamService.Controllers
{
    [Route("IAM/account")]
    [ApiController]
    [Authorize(Roles = "Admin,1,admin")] // Chỉ user có role Admin mới truy cập được
    public class AccountController : ControllerBase
    {
        private readonly IAccountService _accountService;

        public AccountController(IAccountService accountService)
        {
            _accountService = accountService;
        }

        // Lấy danh sách tài khoản có phân trang
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var accounts = await _accountService.GetAllAsync();
            var total = accounts.Count();
            var items = accounts.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            if (!items.Any())
            {
                return NotFound(new { message = "Không có tài khoản nào." });
            }

            return Ok(new
            {
                message = "Lấy danh sách tài khoản thành công.",
                page,
                pageSize,
                total,
                data = items
            });
        }

        // Lấy chi tiết tài khoản theo ID
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var account = await _accountService.GetByIdAsync(id);

            if (account == null)
            {
                return NotFound(new { message = $"Không tìm thấy tài khoản với ID: {id}" });
            }

            return Ok(new { message = "Lấy tài khoản thành công.", data = account });
        }

        // Tạo mới tài khoản
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AccountDto accountDto)
        {
            var createdAccount = await _accountService.CreateAsync(accountDto);
            if (createdAccount == null)
            {
                return NotFound(new { message = "Tạo tài khoản thất bại." });
            }
            return Ok(new { message = "Tạo tài khoản thành công.", data = createdAccount });
        }

        // Cập nhật tài khoản
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] AccountDto accountDto)
        {
            var updatedAccount = await _accountService.UpdateAsync(id, accountDto);

            if (updatedAccount == null)
            {
                return NotFound(new { message = $"Không tìm thấy tài khoản để cập nhật với ID: {id}" });
            }

            return Ok(new { message = "Cập nhật tài khoản thành công.", data = updatedAccount });
        }

        // Xóa tài khoản
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            bool isDeleted = await _accountService.DeleteAsync(id);

            if (!isDeleted)
            {
                return NotFound(new { message = $"Không tìm thấy tài khoản để xóa với ID: {id}" });
            }

            return Ok(new { message = "Xóa tài khoản thành công." });
        }

        [HttpPost("test-email")]
        [AllowAnonymous]
        public async Task<IActionResult> TestEmail([FromServices] IEmailService emailService)
        {
            await emailService.SendEmailAsync(
                "trantanphat2004lph@gmail.com",
                "Kiểm tra gửi mail IAMService",
                "<h3>✅ Gửi mail thành công từ IAMService</h3>"
            );

            return Ok(new { message = "Mail đã gửi thành công" });
        }

        [HttpGet("debug-claims")]
        [AllowAnonymous]
        public IActionResult GetClaims()
        {
            var user = HttpContext.User;
            return Ok(new
            {
                IsAuthenticated = user.Identity?.IsAuthenticated,
                Name = user.Identity?.Name,
                AuthenticationType = user.Identity?.AuthenticationType,
                RoleClaimType = (user.Identity as System.Security.Claims.ClaimsIdentity)?.RoleClaimType,
                Claims = user.Claims.Select(c => new { c.Type, c.Value }).ToList()
            });
        }
    }
}
