using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using TickerQ.Dashboard.Controllers;
using TickerQ.Utilities.Entities;

namespace TickerQ.Dashboard;

public class GenericControllerFeatureProvider<TTimeTicker, TCronTicker> : IApplicationFeatureProvider<ControllerFeature>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        var controller = typeof(TickerQController<TTimeTicker, TCronTicker>).GetTypeInfo();

        if (controller.ContainsGenericParameters)
            return;

        var alreadyAdded = feature.Controllers.Any(c => c.AsType() == controller.AsType());
        if (!alreadyAdded)
        {
            feature.Controllers.Add(controller);
        }
    }
}