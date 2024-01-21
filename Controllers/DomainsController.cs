using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyBGList.Attributes;
using MyBGList.Constants;
using MyBGList.DTO;
using MyBGList.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.Linq.Dynamic.Core;

namespace MyBGList.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class DomainsController : ControllerBase
    {
        private readonly ILogger<DomainsController> _logger;

        private readonly ApplicationDbContext _context;

        public DomainsController(ILogger<DomainsController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        /// <summary>
        /// Gets a list of domains
        /// </summary>
        /// <remarks>Retrives a list of domains with custom paging, sorting, and filtering rules</remarks>
        /// <param name="input">A DTO object that can be used to customize some retrieval parameters</param>
        /// <returns>A RestDTO object containing a list of domains</returns>
        [HttpGet(Name = "GetDomains")]
        [ResponseCache(CacheProfileName = "Any-60")]
        [ManualValidationFilter]
        public async Task<ActionResult<RestDTO<Domain[]>>> Get(
            [FromQuery] RequestDTO<DomainDTO> input)
        {
            if (!ModelState.IsValid)
            {
                var details = new ValidationProblemDetails(ModelState);

                details.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ??
                    HttpContext.TraceIdentifier;

                if (ModelState.Keys.Any(k => k == "PageSize"))
                {
                    details.Type = "https://tools.ietf.org/html/rfc7221#section-6.6.2";
                    details.Status = StatusCodes.Status501NotImplemented;
                    return new ObjectResult(details)
                    {
                        StatusCode = StatusCodes.Status501NotImplemented
                    };
                }
                else
                {
                    details.Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1";
                    details.Status = StatusCodes.Status400BadRequest;
                    return new BadRequestObjectResult(details);
                }
            }

            var query = _context.Domains.AsQueryable();

            if (!string.IsNullOrEmpty(input.FilterQuery))
            {
                query = query.Where(b => b.Name.StartsWith(input.FilterQuery));
            }

            var recordCount = await query.CountAsync();

            query = query
                .OrderBy($"{input.SortColumn} {input.SortOrder}")
                .Skip(input.PageIndex * input.PageSize)
                .Take(input.PageSize);

            return new RestDTO<Domain[]>()
            {
                Data = await query.ToArrayAsync(),
                RecordCount = recordCount,
                PageIndex = input.PageIndex,
                PageSize = input.PageSize,
                Links = new List<LinkDTO>
                {
                    new LinkDTO(Url.Action(
                        null,
                        "Domains",
                        new { input.PageIndex, input.PageSize },
                        Request.Scheme)!,
                        "self",
                        "GET"
                        )
                }
            };
        }

        [Authorize(Roles = RoleNames.Moderator)]
        [HttpPost(Name = "UpdateDomain")]
        [ResponseCache(CacheProfileName = "NoCache")]
        [ManualValidationFilter]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<ActionResult<RestDTO<Domain?>>> Post(DomainDTO model)
        {
            if (!ModelState.IsValid)
            {
                var details = new ValidationProblemDetails(ModelState);

                details.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ??
                    HttpContext.TraceIdentifier;

                if (ModelState.Keys.Any(k => k == "Id" || k == "Name"))
                {
                    details.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3";
                    details.Status = StatusCodes.Status403Forbidden;
                    return new ObjectResult(details)
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                }
                else
                {
                    details.Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1";
                    details.Status = StatusCodes.Status400BadRequest;
                    return new BadRequestObjectResult(details);
                }
            }

            var domain = await _context.Domains
                .Where(b => b.Id == model.Id)
                .FirstOrDefaultAsync();

            if (domain != null)
            {
                if (!string.IsNullOrEmpty(model.Name))
                {
                    domain.Name = model.Name;
                }
                domain.LastModifiedDate = DateTime.Now;
                _context.Domains.Update(domain);
                await _context.SaveChangesAsync();
            }

            return new RestDTO<Domain?>()
            {
                Data = domain,
                Links = new List<LinkDTO>
                {
                    new LinkDTO(
                        Url.Action(
                            null,
                            "Domains",
                            model,
                            Request.Scheme)!,
                        "self",
                        "POST")
                }
            };
        }

        [Authorize(Roles = RoleNames.Administrator)]
        [HttpDelete(Name = "DeleteDomain")]
        [ResponseCache(CacheProfileName = "NoCache")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<RestDTO<Domain[]?>> Delete(string ids)
        {
            var idArray = ids.Split(",").Select(x => int.Parse(x));
            var deleteDomainList = new List<Domain>();

            foreach (var id in idArray)
            {
                var domain = await _context.Domains
                    .Where(b => b.Id == id)
                    .FirstOrDefaultAsync();

                if (domain != null)
                {
                    deleteDomainList.Add(domain);
                    _context.Domains.Remove(domain);
                    await _context.SaveChangesAsync();
                }
            }

            return new RestDTO<Domain[]?>()
            {
                Data = deleteDomainList.Count > 0 ? deleteDomainList.ToArray() : null,
                Links = new List<LinkDTO>
                {
                    new LinkDTO(
                        Url.Action(
                            null,
                            "Domains",
                            ids,
                            Request.Scheme)!,
                        "self",
                        "DELETE")
                }
            };
        }
    }
}
