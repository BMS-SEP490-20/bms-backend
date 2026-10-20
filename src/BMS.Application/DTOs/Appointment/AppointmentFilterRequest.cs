using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Appointment
{
    /// <summary>
    /// UC23 – Filter and pagination request parameters for viewing appointments.
    /// </summary>
    public class AppointmentFilterRequest : IValidatableObject
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? DoctorId { get; set; }
        public string? Status { get; set; }
        public string? Keyword { get; set; }
        public string? PatientId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Page phải lớn hơn hoặc bằng 1.")]
        public int Page { get; set; } = 1;

        [Range(1, 100, ErrorMessage = "PageSize phải từ 1 đến 100.")]
        public int PageSize { get; set; } = 10;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (FromDate.HasValue && ToDate.HasValue)
            {
                if (ToDate.Value.Date < FromDate.Value.Date)
                {
                    yield return new ValidationResult(
                        "ToDate phải lớn hơn hoặc bằng FromDate.",
                        new[] { nameof(ToDate) });
                }
                else if ((ToDate.Value.Date - FromDate.Value.Date).TotalDays > 90)
                {
                    yield return new ValidationResult(
                        "Khoảng cách tìm kiếm tối đa không quá 90 ngày.",
                        new[] { nameof(ToDate), nameof(FromDate) });
                }
            }

            if (!string.IsNullOrWhiteSpace(Keyword) && Keyword.Length > 100)
            {
                yield return new ValidationResult(
                    "Từ khóa tìm kiếm không được vượt quá 100 ký tự.",
                    new[] { nameof(Keyword) });
            }
        }
    }
}
