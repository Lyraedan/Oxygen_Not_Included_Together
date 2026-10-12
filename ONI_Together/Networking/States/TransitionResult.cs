namespace ONI_Together.Networking.States
{
	public readonly struct TransitionResult
	{
		public bool Success { get; }
		public string Reason { get; }

		private TransitionResult(bool success, string reason)
		{
			Success = success;
			Reason = reason;
		}

		public static TransitionResult Accepted() => new(true, null);
		public static TransitionResult Rejected(string reason) => new(false, reason);
	}
}
