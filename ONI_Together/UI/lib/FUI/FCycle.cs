using ONI_Together.DebugTools;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI.lib.UIcmp //Source: Aki & Sgt_Imalas
{
	public class FCycle : KMonoBehaviour
	{
		public event System.Action<Option> OnChange;

		[SerializeField]
		public FButton leftArrow;

		[SerializeField]
		public FButton rightArrow;

		[SerializeField]
		public LocText label;

		[SerializeField]
		public LocText description;

		private int currentIndex = 0;

		[SerializeField]
		public List<Option> Options;

		[SerializeField]
		public Func<string, string> DescriptionFormatter = null;
		[SerializeField]
		public Func<string, string> NameFormatter = null;

		[Serializable]
		public class Option
		{
			public string id;
			public string title;
			public string description;

			public Option(string id, string title, string description = null)
			{
				this.id = id;
				this.title = title;
				this.description = description;
			}
		}


		public void Initialize(FButton leftButton, FButton rightButton, LocText label, LocText description = null)
		{
			leftArrow = leftButton;
			rightArrow = rightButton;

			this.label = label;
			this.description = description;

			leftArrow.OnClick += CycleLeft;
			rightArrow.OnClick += CycleRight;
		}


		public override void OnSpawn()
		{
			base.OnSpawn();
			UpdateLabel();
		}
		private bool _isInteractable = true;

		public bool IsInteractable
		{
			get { return _isInteractable; }
			set
			{
				_isInteractable = value;
				leftArrow.SetInteractable(value);
				rightArrow.SetInteractable(value);
			}
		}
		public void SetInteractable(bool interactable)
		{
			IsInteractable = interactable;
		}
		private bool HasOptions => Options.Count > 0;

		public void SetValueById(string id)
		{
			var index = Options.FindIndex(x => x.id == id);
			if (currentIndex == index)
			{
				return;
			}
			if (index != -1)
			{
				currentIndex = index;
			}
			else
			{
				Debug.LogWarning($"Invalid option ID given \"{id}\"");
				currentIndex = 0;
			}
			UpdateLabel();
		}

		public Option Value
		{
			get => Options.Count >= currentIndex ? Options[currentIndex] : default;

			set
			{
				var index = Options.FindIndex(x => x == value);

				if (currentIndex == index)
				{
					return;
				}

				if (index != -1)
				{
					currentIndex = index;
				}
				else
				{
					Debug.LogWarning($"Invalid option ID given \"{value}\"");
					currentIndex = 0;
				}

				UpdateLabel();
			}
		}

		public void CycleLeft()
		{
			if (HasOptions && IsInteractable)
			{
				currentIndex = (currentIndex + Options.Count - 1) % Options.Count;
				UpdateLabel();
				OnChange?.Invoke(Value);
			}
		}

		public void CycleRight()
		{
			if (HasOptions && IsInteractable)
			{
				currentIndex = (currentIndex + 1) % Options.Count;
				UpdateLabel();
				OnChange?.Invoke(Value);
			}
		}

		public void UpdateLabel()
		{
			if (Options.Count >= currentIndex)
			{
				Value = Options[currentIndex];

				string title = Options[currentIndex].title;
				if (NameFormatter != null)
				{
					title = NameFormatter(title);
				}
				label.SetText(title);

				if (description != null)
				{
					string desc = Options[currentIndex].description;
					if (DescriptionFormatter != null)
					{
						desc = DescriptionFormatter(desc);
					}
					description.SetText(desc);
				}
			}
		}

		public void SetDescriptionFormatter(Func<string, string> formatDescription)
		{
			DescriptionFormatter = formatDescription;
		}
		public void SetNameFormatter(Func<string, string> formatName)
		{
			NameFormatter = formatName;
		}

	}
}