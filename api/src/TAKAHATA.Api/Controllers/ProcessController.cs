using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAKAHATA.Application.MaintenanceProcess.Queries;
using TAKAHATA.Application.Services;
using TAKAHATA.Domain.Common.Constant;

namespace TAKAHATA.Api.Controllers
{
    [Authorize(Roles = $"{Role.Admin}, {Role.User}")]
    public class ProcessController : ApiController
    {
        private ISender _sender;

        public ProcessController(ISender sender)
        {
            _sender = sender;
        }

        [HttpPost("get-part-process-by-machine")]
        public async Task<IActionResult> GetPartProcessByMachine(ProcessesOfMachineQuery query)
        {
            var effectiveQuery = User.IsInRole(Role.User) && !User.IsInRole(Role.Admin)
                ? query with { PlantDate = VietnamTime.Today }
                : query;
            var result = await _sender.Send(effectiveQuery);

            return result.Match(
                r => Ok(r),
                e => Problem(e));
        }
    }
}
