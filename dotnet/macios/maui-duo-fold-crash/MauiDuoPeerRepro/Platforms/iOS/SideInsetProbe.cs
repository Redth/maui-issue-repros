using CoreGraphics;
using UIKit;

namespace MauiDuoPeerRepro;

/// <summary>
/// Insets a vertical VirtualListView's rows clear of the side safe areas (the Duo cover display's
/// bar column) through its flow layout's SectionInset, so the list itself can run to the screen edge.
/// A zero-height probe subview reports safe-area changes (folding moves them).
/// </summary>
static class SideInsetProbe
{
	public static void Register()
	{
		VirtualListViewHandler.ViewMapper.AppendToMapping("ReproSideInsets", (handler, view) =>
		{
			if (handler.PlatformView is not UICollectionView collectionView
				|| collectionView.CollectionViewLayout is not UICollectionViewFlowLayout
				|| collectionView.Subviews.Any(v => v is Probe))
				return;
			collectionView.AddSubview(new Probe(collectionView));
		});
	}

	sealed class Probe : UIView
	{
		readonly WeakReference<UICollectionView> _collectionView;
		nfloat _left = -1, _right = -1;
		bool _queued;

		public Probe(UICollectionView collectionView) : base(new CGRect(0, 0, collectionView.Bounds.Width, 0))
		{
			_collectionView = new(collectionView);
			AutoresizingMask = UIViewAutoresizing.FlexibleWidth;
			UserInteractionEnabled = false;
		}

		public override void SafeAreaInsetsDidChange()
		{
			base.SafeAreaInsetsDidChange();
			if (SafeAreaInsets.Left == _left && SafeAreaInsets.Right == _right)
				return;
			_left = SafeAreaInsets.Left;
			_right = SafeAreaInsets.Right;
			Queue();
		}

		public override void MovedToWindow()
		{
			base.MovedToWindow();
			Queue();
		}

		void Queue()
		{
			if (_queued)
				return;
			_queued = true;
			MainThread.BeginInvokeOnMainThread(() => { _queued = false; Update(); });
		}

		void Update()
		{
			if (!_collectionView.TryGetTarget(out var cv) || cv.Window is null || cv.CollectionViewLayout is not UICollectionViewFlowLayout layout)
				return;
			var safe = cv.SafeAreaInsets;
			var adjusted = cv.AdjustedContentInset;
			var content = cv.ContentInset;
			var left = (nfloat)Math.Max(0, safe.Left - (adjusted.Left - content.Left));
			var right = (nfloat)Math.Max(0, safe.Right - (adjusted.Right - content.Right));
			var inset = layout.SectionInset;
			if (Math.Abs(inset.Left - left) < 0.5 && Math.Abs(inset.Right - right) < 0.5)
				return;
			layout.SectionInset = new UIEdgeInsets(inset.Top, left, inset.Bottom, right);
			layout.InvalidateLayout();
		}
	}
}
