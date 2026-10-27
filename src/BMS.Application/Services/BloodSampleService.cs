using TestOrder.Models.DTO;
using TestOrder.Repositories.Interface;
using TestOrder.Services.Interface;
using Testorder_service.Models.Entities;

namespace TestOrder.Services.Implement
{
    public class BloodSampleService : IBloodSampleService
    {
        private readonly IBloodSampleRepository _repo;

        public BloodSampleService(IBloodSampleRepository repo)
        {
            _repo = repo;
        }

        public async Task<IEnumerable<BloodSampleReadDto>> GetAllAsync()
        {
            var list = await _repo.GetAllAsync();
            return list.Select(x => new BloodSampleReadDto
            {
                SampleId = x.SampleId,
                AppointmentId = x.AppointmentId,
                SampleType = x.SampleType,
                CollectedAt = x.CollectedAt,
                Volume = x.Volume,
                Status = x.Status,
                StorageLocation = x.StorageLocation,
                Notes = x.Notes,
                LabUserId = x.LabUserId
            });
        }

        public async Task<BloodSampleReadDto?> GetByIdAsync(string id)
        {
            var x = await _repo.GetByIdAsync(id);
            if (x == null) return null;

            return new BloodSampleReadDto
            {
                SampleId = x.SampleId,
                AppointmentId = x.AppointmentId,
                SampleType = x.SampleType,
                CollectedAt = x.CollectedAt,
                Volume = x.Volume,
                Status = x.Status,
                StorageLocation = x.StorageLocation,
                Notes = x.Notes,
                LabUserId = x.LabUserId

            };
        }

        public async Task<BloodSampleReadDto> CreateAsync(BloodSampleCreateDto dto)
        {
            var random = new Random();
            var entity = new BloodSample
            {
                SampleId = $"SA{random.Next(0, 999999):D6}",
                AppointmentId = dto.AppointmentId,
                SampleType = dto.SampleType,
                CollectedAt = dto.CollectedAt,
                Volume = dto.Volume,
                Status = dto.Status,
                StorageLocation = dto.StorageLocation,
                Notes = dto.Notes,
                LabUserId = dto.LabUserId

            };

            var created = await _repo.CreateAsync(entity);

            return new BloodSampleReadDto
            {
                SampleId = created.SampleId,
                AppointmentId = created.AppointmentId,
                SampleType = created.SampleType,
                CollectedAt = created.CollectedAt,
                Volume =  created.Volume,
                Status = created.Status,
                StorageLocation = created.StorageLocation,
                Notes = created.Notes,
                LabUserId = created.LabUserId
            };
        }

        public async Task<bool> UpdateAsync(string id, BloodSampleUpdateDto dto)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null) return false;

            entity.SampleType = dto.SampleType ?? entity.SampleType;
            entity.CollectedAt = dto.CollectedAt ?? entity.CollectedAt;
            entity.Volume = dto.Volume ?? entity.Volume;
            entity.Status = dto.Status ?? entity.Status;
            entity.StorageLocation = dto.StorageLocation ?? entity.StorageLocation;
            entity.Notes = dto.Notes ?? entity.Notes;
            entity.LabUserId = dto.LabUserId ?? entity.LabUserId;
            await _repo.UpdateAsync(entity);
            return true;
        }

        public async Task<bool> DeleteAsync(string id)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null) return false;

            await _repo.DeleteAsync(entity);
            return true;
        } 
        public async Task<bool> ValidateSampleAsync(string id, string status, string notes, string labUserId)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null) return false;

            entity.Status = status;
            entity.Notes = notes;
            if (!string.IsNullOrEmpty(labUserId)) {
                entity.LabUserId = labUserId;
            }
            await _repo.UpdateAsync(entity);
            return true;
        }
        public async Task<IEnumerable<BloodSampleReadDto>> GetBloodSampleByAppointmentIdAsync(string appointmentId)
        {
            var list = await _repo.GetByAppointmentIdAsync(appointmentId);
            return list.Select(x => new BloodSampleReadDto
            {
                SampleId = x.SampleId,
                AppointmentId = x.AppointmentId,
                SampleType = x.SampleType,
                CollectedAt = x.CollectedAt,
                Volume = x.Volume,
                Status = x.Status,
                StorageLocation = x.StorageLocation,
                Notes = x.Notes,
                LabUserId = x.LabUserId
            });
        }

    }
}

