using System.Threading;
using System.Threading.Tasks;

namespace Core.Domain.Planning;

public interface IEditPlanner
{
	Task<EditPlanDocument> CreatePlanAsync(EditPlanningRequest request, CancellationToken cancellationToken);
}
