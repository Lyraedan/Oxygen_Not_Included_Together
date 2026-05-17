/// Credit Ziboo
///Sourced from - https://github.com/brogan89/MinMaxSlider

/* # Unity UI Extensions License (BSD3)

Copyright (c) 2019

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer
   in the documentation and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived
   from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY,
OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE. */



using System;

namespace UI.lib.UI.FUI.Unity_UI_Extensions.Scripts.Utilities
{
	[Serializable]
	public struct MinMaxValues
	{
		/// <summary>
		/// Floating point tolerance
		/// </summary>
		public const float FLOAT_TOL = 0.01f;

		public float minValue, maxValue, minLimit, maxLimit;
		public static MinMaxValues DEFUALT = new MinMaxValues(25, 75, 0, 100);

		public MinMaxValues(float minValue, float maxValue, float minLimit, float maxLimit)
		{
			this.minValue = minValue;
			this.maxValue = maxValue;
			this.minLimit = minLimit;
			this.maxLimit = maxLimit;
		}

		/// <summary>
		/// Constructor for when values equal limits
		/// </summary>
		/// <param name="minValue"></param>
		/// <param name="maxValue"></param>
		public MinMaxValues(float minValue, float maxValue)
		{
			this.minValue = minValue;
			this.maxValue = maxValue;
			this.minLimit = minValue;
			this.maxLimit = maxValue;
		}

		public bool IsAtMinAndMax()
		{
			return Math.Abs(minValue - minLimit) < FLOAT_TOL && Math.Abs(maxValue - maxLimit) < FLOAT_TOL;
		}

		public override string ToString()
		{
			return $"Values(min:{minValue}, max:{maxValue}) | Limits(min:{minLimit}, max:{maxLimit})";
		}
	}
}
