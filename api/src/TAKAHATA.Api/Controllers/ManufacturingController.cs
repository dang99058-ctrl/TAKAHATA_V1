using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAKAHATA.Application.Manufacturing.Commands;
using TAKAHATA.Domain.Common.Constant;

namespace TAKAHATA.Api.Controllers
{

    public class ManufacturingController : ApiController
    {
        private readonly ISender _sender;

        public ManufacturingController(ISender sender)
        {
            _sender = sender;
        }

        [HttpPost("start")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> Start(StartCommand request)
        {
            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("pause")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> Pause(PauseCommand request)
        {
            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("complete")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> Complete(CompleteCommand request)
        {
            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("begin-setting-up")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> BeginSettingUp(BeginSettingUpCommand request)
        {
            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

        [HttpPost("cancel-setting-up")]
        [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
        public async Task<IActionResult> CancelSettingUp(CancelSettingUpCommand request)
        {
            var result = await _sender.Send(request);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }

    }
}
