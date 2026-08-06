using System;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// A working card on the Z3 fact wall: the one <see cref="IMemoryFactVm"/> implementation that
    /// actually <i>does</i> the three things the design promises per card — 📌 pin, ✏ edit,
    /// 🗑 forget — instead of holding still for the designer.
    ///
    /// <para>The rules encoded here are the ones the wall would otherwise get wrong:</para>
    /// <list type="bullet">
    ///   <item><b>Boundaries are not pinnable.</b> They already sort first and are never evicted;
    ///   offering a pin on them implies they could sink, which is exactly the wrong promise for a
    ///   consent record. They stay editable and forgettable — a boundary the user no longer wants
    ///   remembered must be removable.</item>
    ///   <item><b>The dormant promise card is inert.</b> It is copy, not a fact: nothing to pin,
    ///   edit or forget.</item>
    ///   <item><b>Committing a blank edit cancels instead of erasing.</b> Forgetting a fact is an
    ///   explicit, separately-worded action; it must never happen by clearing a textbox.</item>
    ///   <item><b>An edited fact says so.</b> On commit the provenance line flips to
    ///   <see cref="UserEditedMetaLabel"/> when one was supplied (doc 01: the source becomes
    ///   <c>user-edited</c> and the salience floor applies).</item>
    /// </list>
    ///
    /// <para>Pinning raises <c>PropertyChanged</c> for <see cref="IsPinned"/>; the owning diary
    /// viewmodel listens for that and re-runs <see cref="FactOrdering.Project"/>, which is what
    /// makes a pinned card visibly jump up the wall. The card itself never sorts anything.</para>
    /// </summary>
    public sealed class MemoryFactCard : CompanionObservable, IMemoryFactVm
    {
        private string _text;
        private string _metaLabel;
        private bool _isPinned;
        private bool _isEditing;
        private string _editText = string.Empty;

        public MemoryFactCard(
            string text,
            string kindKey,
            string kindLabel,
            string metaLabel = "",
            bool isBoundary = false,
            bool isPinned = false,
            bool isDormant = false,
            string? id = null)
        {
            _text = text ?? string.Empty;
            _metaLabel = metaLabel ?? string.Empty;
            _isPinned = isPinned;
            Id = id ?? Guid.NewGuid().ToString("N");
            KindKey = string.IsNullOrWhiteSpace(kindKey) ? "moment" : kindKey;
            KindLabel = kindLabel ?? string.Empty;
            IsBoundary = isBoundary;
            IsDormant = isDormant;

            PinCommand = new CompanionRelayCommand(TogglePin, () => CanPin);
            EditCommand = new CompanionRelayCommand(BeginEdit, () => CanEdit);
            ForgetCommand = new CompanionRelayCommand(Forget, () => CanForget);
            CommitEditCommand = new CompanionRelayCommand(CommitEdit);
        }

        public string Id { get; }
        public string KindKey { get; }
        public string KindLabel { get; }
        public bool IsBoundary { get; }
        public bool IsDormant { get; }

        /// <summary>Provenance line to show once the user has rewritten the fact by hand.</summary>
        public string? UserEditedMetaLabel { get; init; }

        /// <summary>
        /// Raised by <see cref="ForgetCommand"/>. The owner removes the card and re-projects; the
        /// card deliberately cannot remove itself from a list it does not own.
        /// </summary>
        public Action<MemoryFactCard>? Forgotten { get; set; }

        public string Text
        {
            get => _text;
            private set => Set(ref _text, value);
        }

        public string MetaLabel
        {
            get => _metaLabel;
            private set => Set(ref _metaLabel, value);
        }

        public bool IsPinned
        {
            get => _isPinned;
            private set => Set(ref _isPinned, value);
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (value && !CanEdit) return;             // dormant copy is never editable
                if (!Set(ref _isEditing, value)) return;
                if (value) EditText = Text;                // opening the box seeds it with the fact
            }
        }

        public string EditText
        {
            get => _editText;
            set => Set(ref _editText, value);
        }

        public ICommand PinCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ForgetCommand { get; }
        public ICommand CommitEditCommand { get; }

        /// <summary>A boundary already sorts first and the promise card is not a fact.</summary>
        public bool CanPin => !IsBoundary && !IsDormant;
        public bool CanEdit => !IsDormant;
        public bool CanForget => !IsDormant;

        /// <summary>Boundary ▸ pinned ▸ normal ▸ dormant. Same ladder the projection uses.</summary>
        public int SortRank => FactOrdering.SortRank(IsBoundary, IsPinned, IsDormant);

        private void TogglePin()
        {
            if (!CanPin) return;
            IsPinned = !IsPinned;
        }

        private void BeginEdit()
        {
            if (!CanEdit) return;
            IsEditing = true;
        }

        /// <summary>
        /// Applies the inline edit. A blank or whitespace-only box is treated as "never mind" —
        /// see the class remarks: erasing a memory is <see cref="ForgetCommand"/>'s job, and it
        /// asks first.
        /// </summary>
        private void CommitEdit()
        {
            if (!_isEditing) return;

            string trimmed = (EditText ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _isEditing = false;
                Raise(nameof(IsEditing));
                return;
            }

            bool changed = !string.Equals(trimmed, Text, StringComparison.Ordinal);
            Text = trimmed;
            if (changed && !string.IsNullOrWhiteSpace(UserEditedMetaLabel)) MetaLabel = UserEditedMetaLabel!;

            _isEditing = false;
            Raise(nameof(IsEditing));
        }

        private void Forget()
        {
            if (!CanForget) return;
            _isEditing = false;
            Raise(nameof(IsEditing));
            Forgotten?.Invoke(this);
        }
    }
}
