using ErrorOr;
using MapsterMapper;
using MediatR;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Contracts.Common;
using TAKAHATA.Contracts.IndexAndPart;

namespace TAKAHATA.Application.MaintenanceProcess.Queries
{
    public class ProcessesOfMachineQueryHandler : IRequestHandler<ProcessesOfMachineQuery, ErrorOr<SearchResponse<List<ManufacturingProcessResponse>>>>
    {
        private readonly IMapper _mapper;

        private readonly IPartProcessRepository _partProcessRepository;

        public ProcessesOfMachineQueryHandler(
            IMapper mapper,
            IPartProcessRepository partProcessRepository)
        {
            _mapper = mapper;
            _partProcessRepository = partProcessRepository;
        }

        public async Task<ErrorOr<SearchResponse<List<ManufacturingProcessResponse>>>> Handle(
            ProcessesOfMachineQuery request,
            CancellationToken cancellationToken)
        {
            return await _partProcessRepository
                .SearchByMachineIdAsync(
                    request.MachineId,
                    request.Page,
                    request.PageSize,
                    request.PlantDate);
        }
    }
}
