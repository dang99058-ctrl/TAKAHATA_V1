using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Application.Settings.Commands;
using TAKAHATA.Domain.Common.Constant;

namespace TAKAHATA.Api.Controllers
{
    [Authorize(Roles = Role.Admin)]
    public class SettingsController : ApiController
    {
        private readonly ISender _sender;
        private readonly IPLCSettingsService _pLCSettingsService;
        private readonly IUnitOfWorkRepository _unitOfWorkRepository;

        public SettingsController(
            ISender sender,
            IPLCSettingsService pLCSettingsService,
            IUnitOfWorkRepository unitOfWorkRepository)
        {
            _sender = sender;
            _pLCSettingsService = pLCSettingsService;
            _unitOfWorkRepository = unitOfWorkRepository;
        }

        [HttpPost("update")]
        public async Task<IActionResult> Update(UpdateSettingsCommand request)
        {
            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query()
        {
            var response = new
            {
                IpAddress = _pLCSettingsService.GetIpAddress(),
                Port = _pLCSettingsService.GetPort(),
            };

            return Ok(response);
        }

        [HttpPost("write-machine-bits")]
        public async Task<IActionResult> WriteMachineBits(WriteMachineBitsCommand request)
        {
            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("read-machine-bits")]
        public async Task<IActionResult> ReadMachineBits(ReadMachineBitsCommand request)
        {
            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("clear-pending-daily-cutover")]
        public async Task<IActionResult> ClearPendingDailyCutover(
            ClearPendingDailyCutoverRequest request)
        {
            if (request.MachineId <= 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Máy không hợp lệ."
                });
            }

            var reportDate = await _unitOfWorkRepository
                .ClearPendingDailyCutoverAsync(request.MachineId);
            if (!reportDate.HasValue)
            {
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Không có yêu cầu chốt ngày đang chờ",
                    Detail = $"Máy Id={request.MachineId} hiện không có phản hồi chốt ngày đang chờ."
                });
            }

            return Ok(new
            {
                request.MachineId,
                ReportDate = reportDate.Value
            });
        }

    }

    public sealed record ClearPendingDailyCutoverRequest(int MachineId);
}
