namespace ONI_Together.Networking.States
{
	public enum CursorState
	{
		NONE,
		SELECT,        // Default inspect/select tool
		BUILD,         // Place buildings
		DIG,           // Dig terrain
		CANCEL,        // Cancel tasks
		DECONSTRUCT,   // Deconstruct buildings
		PRIORITIZE,    // Set priority levels >= 5
		DEPRIORITIZE,  // Set priority levels < 5
		SWEEP,         // Mark items to be swept
		MOP,           // Mop up liquids
		HARVEST,       // Harvest crops
		DISINFECT,     // Disinfect areas
		ATTACK,        // Attack critters or objects
		CAPTURE,       // Capture critters
		WRANGLE,       // Wrangle critters for transport
		EMPTY_PIPE,    // Empty contents of pipes
		DISCONNECT,    // Disconnect wires, pipes etc
		CLEAR_FLOOR,   // Mark debris for removal
		MOVE_TO        // Direct duplicants to move
	}
}
