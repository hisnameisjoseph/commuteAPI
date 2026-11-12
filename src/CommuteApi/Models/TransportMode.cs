using System.ComponentModel.DataAnnotations;

namespace CommuteApi.Models;

/// <summary>
/// The modes ptv-display can show. Values are PTV <c>route_type</c> codes, so they are
/// stored and returned as-is. Trams (1), V/Line (3) and Night Bus (4) are deliberately
/// absent: the board has no card for them.
/// </summary>
public enum TransportMode
{
    [Display(Name = "Train")]
    Train = 0,

    [Display(Name = "Bus")]
    Bus = 2,
}
