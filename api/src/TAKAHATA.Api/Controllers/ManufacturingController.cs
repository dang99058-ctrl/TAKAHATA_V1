using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Services;
using TAKAHATA.Application.Manufacturing.Commands;
using TAKAHATA.Contracts.Common;
using TAKAHATA.Domain.Common.Constant;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Api.Controllers
{

    public class ManufacturingController : ApiController
    {
        private readonly ISender _sender;
        private readonly IPartProcessRepository _partProcessRepository;

        public ManufacturingController(
            ISender sender,
            IPartProcessRepository partProcessRepository)
        {
            _sender = sender;
            _partProcessRepository = partProcessRepository;
        }

        [HttpPost("start")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> Start(StartCommand request)
        {
            var dateValidation = await ValidateCurrentPlantDateAsync(request.ProcessId);
            if (dateValidation != null)
                return dateValidation;

            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("pause")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> Pause(PauseCommand request)
        {
            var dateValidation = await ValidateCurrentPlantDateAsync(request.ProcessId);
            if (dateValidation != null)
                return dateValidation;

            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("complete")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> Complete(CompleteCommand request)
        {
            var dateValidation = await ValidateCurrentPlantDateAsync(request.ProcessId);
            if (dateValidation != null)
                return dateValidation;

            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("begin-setting-up")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> BeginSettingUp(BeginSettingUpCommand request)
        {
            var dateValidation = await ValidateCurrentPlantDateAsync(request.ProcessId);
            if (dateValidation != null)
                return dateValidation;

            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("cancel-setting-up")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> CancelSettingUp(CancelSettingUpCommand request)
        {
            var dateValidation = await ValidateCurrentPlantDateAsync(request.ProcessId);
            if (dateValidation != null)
                return dateValidation;

            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        private async Task<IActionResult?> ValidateCurrentPlantDateAsync(int processId)
        {
            if (!User.IsInRole(Role.User) || User.IsInRole(Role.Admin))
                return null;

            var process = await _partProcessRepository.GetByIdAsync(processId);
            var today = VietnamTime.Today;
            if (process == null || process.PlantDate == today)
                return null;

            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Chỉ được thao tác công đoạn trong ngày hiện tại",
                Detail = $"Công đoạn thuộc ngày {process.PlantDate:dd/MM/yyyy}; ngày hệ thống là {today:dd/MM/yyyy}."
            });
        }
    }
}
