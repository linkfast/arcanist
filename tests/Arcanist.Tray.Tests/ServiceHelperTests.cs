namespace Arcanist.Tray.Tests;

public sealed class ServiceHelperTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string Ace = "(A;;RPWPLCLORC;;;S-1-5-21-1-2-3-1001)";

    [Fact]
    public void Control_rights_go_at_the_end_of_the_dacl_before_the_sacl()
    {
        const string sddl = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;IU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";

        Assert.Equal(
            "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;IU)" + Ace + "S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)",
            ServiceHelper.GrantControl(sddl, Sid));
    }

    [Fact]
    public void Control_rights_are_appended_when_there_is_no_sacl()
    {
        Assert.Equal("D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)" + Ace, ServiceHelper.GrantControl("D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)", Sid));
    }

    [Fact]
    public void Granting_twice_changes_nothing()
    {
        var once = ServiceHelper.GrantControl("D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)", Sid);

        Assert.Equal(once, ServiceHelper.GrantControl(once, Sid));
    }
}
