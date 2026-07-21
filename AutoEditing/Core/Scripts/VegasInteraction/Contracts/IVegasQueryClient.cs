using System.Threading.Tasks;

namespace Core.Scripts;

internal interface IVegasQueryClient
{
	Task<TResult> QueryAsync<TResult>(IVegasQuery<TResult> query);
}
