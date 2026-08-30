using System;
using System.Collections.Generic;
using System.Text;

namespace ONI_Together
{
	///<summary>
	///An attribute placed on an a method or field that is reflected for by the mod api.
	///Items marked by this property must not be renamed or have their parameters changed in order to not break the api.
	///If that becomes a necessary change, keep an obsolete marked version with the old headers that points at the new implementation.
	/// </summary>
	internal class API_Method : Attribute
	{
	}
}
