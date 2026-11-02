// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.About;

namespace TwinPanel.Core.Tests;

public class AboutTests
{
    [Fact]
    public void Every_component_has_role_license_and_link()
    {
        foreach (var c in ThirdPartyComponents.Shipped.Concat(ThirdPartyComponents.NotShipped))
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Role));
            Assert.False(string.IsNullOrWhiteSpace(c.License));
            Assert.StartsWith("https://", c.Url);
        }
    }

    [Fact]
    public void No_component_listed_twice()
    {
        var all = ThirdPartyComponents.Shipped.Concat(ThirdPartyComponents.NotShipped).Select(c => c.Name).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
