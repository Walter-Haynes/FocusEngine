// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

using Xenko.Input;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Games;

namespace Xenko.UI.Controls
{
    /// <summary>
    /// Represents a scroll viewer.
    /// A scroll viewer element has an infinite virtual size defined by its <see cref="ScrollingMode"/>.
    /// The user can move in that virtual size by touching and panning on the screen.
    /// </summary>
    [DataContract(nameof(ScrollViewer))]
    [DataContractMetadataType(typeof(ScrollViewerMetadata))]
    [DebuggerDisplay("ScrollViewer - Name={Name}")]
    public class ScrollViewer : ContentControl
    {
        private static readonly Dictionary<ScrollingMode, int[]> ScrollModeToDirectionIndices = new Dictionary<ScrollingMode, int[]>
        {
            { ScrollingMode.None, new int[0] },
            { ScrollingMode.Horizontal, new[] { 0 } },
            { ScrollingMode.Vertical, new[] { 1 } },
            { ScrollingMode.InDepth, new[] { 2 } },
            { ScrollingMode.HorizontalVertical, new[] { 0, 1 } },
            { ScrollingMode.VerticalInDepth, new[] { 1, 2 } },
            { ScrollingMode.InDepthHorizontal, new[] { 2, 0 } },
        };

        private static readonly HashSet<ScrollingMode>[] OrientationToSupportedScrollingModes =
        {
            new HashSet<ScrollingMode> { ScrollingMode.Horizontal, ScrollingMode.HorizontalVertical, ScrollingMode.InDepthHorizontal },
            new HashSet<ScrollingMode> { ScrollingMode.HorizontalVertical, ScrollingMode.Vertical, ScrollingMode.VerticalInDepth },
            new HashSet<ScrollingMode> { ScrollingMode.VerticalInDepth, ScrollingMode.InDepthHorizontal, ScrollingMode.InDepth }
        };

        private static Color transparent = Color.Transparent;

        private const float ScrollBarHidingSpeed = 1f;

        /// <summary>
        /// The current offsets (in virtual pixels) generated by the scrolling on the <see cref="ContentControl.Content"/> element.
        /// </summary>
        protected Vector3 ScrollOffsets;

        /// <summary>
        /// The current speed of the scrolling in virtual pixels.
        /// </summary>
        protected Vector3 CurrentScrollingSpeed;

        /// <summary>
        /// Indicate if the user is currently touching the scroll viewer and performing a scroll gesture with its finger.
        /// </summary>
        protected bool IsUserScrollingViewer { get; private set; }

        /// <summary>
        /// The viewport of the <see cref="ScrollViewer"/> in virtual pixels.
        /// </summary>
        public Vector3 ViewPort { get; private set; }

        /// <summary>
        /// Gets or sets the color of the scrolling bar.
        /// </summary>
        /// <userdoc>The color of the scrolling bar.</userdoc>
        [DataMember]
        [Display(category: AppearanceCategory)]
        public Color ScrollBarColor { get; set; } = new Color(0.1f, 0.1f, 0.1f, 1f);

        /// <summary>
        /// Gets or sets how transparent the bar will go when not used
        /// </summary>
        [DataMember]
        [Display(category: AppearanceCategory)]
        [DefaultValue(0.0f)]
        public float ScrollBarFadeAlpha { get; set; } = 0.0f;

        /// <summary>
        /// Gets or sets the scrolling bar thickness in virtual pixels.
        /// </summary>
        /// <userdoc>The scrolling bar thickness in virtual pixels.</userdoc>
        [DataMember]
        [Display(category: AppearanceCategory)]
        [DefaultValue(6.0f)]
        public float ScrollBarThickness { get; set; } = 6.0f;

        /// <summary>
        /// The viewer allowed scrolling mode.
        /// </summary>
        /// <userdoc>The viewer allowed scrolling mode.</userdoc>
        [DataMember]
        [Display(category: BehaviorCategory)]
        [DefaultValue(ScrollingMode.Horizontal)]
        public ScrollingMode ScrollMode
        {
            get { return scrollMode; }
            set
            {
                if (scrollMode == value)
                    return;

                scrollMode = value;
                OnScrollModeChanged();
            }
        }

        /// <summary>
        /// Gets or sets the threshold distance over which a touch move starts scrolling.
        /// </summary>
        /// <userdoc>The threshold distance over which a touch move starts scrolling.</userdoc>
        [DataMember]
        [Display(category: BehaviorCategory)]
        [DefaultValue(10.0f)]
        public float ScrollStartThreshold { get; set; } = 10.0f;

        /// <summary>
        /// The automatic deceleration of the scroll after the user remove its finger from the screen. The unit is in virtual pixels.
        /// </summary>
        /// <userdoc>The automatic deceleration of the scroll after the user remove its finger from the screen. The unit is in virtual pixels.</userdoc>
        [DataMember]
        [Display(category: BehaviorCategory)]
        [DefaultValue(1500.0f)]
        public float Deceleration
        {
            get { return deceleration; }
            set
            {
                if (float.IsNaN(value))
                    return;
                deceleration = value;
            }
        }

        /// <summary>
        /// Gets or sets the scrolling behavior on touches. True to allow the user to scroll by touching, false to forbid it.
        /// </summary>
        /// <userdoc>True to allow the user to scroll by touching, false to forbid it.</userdoc>
        [DataMember]
        [Display(category: BehaviorCategory)]
        [DefaultValue(true)]
        public bool TouchScrollingEnabled
        {
            get { return touchScrollingEnabled; }
            set
            {
                if (touchScrollingEnabled == value)
                    return;

                touchScrollingEnabled = value;
                OnTouchScrollingEnabledChanged();
            }
        }

        /// <summary>
        /// Scroll speed multiplier. Can be negative to reverse scrolling.
        /// </summary>
        [DataMember]
        [Display(category: BehaviorCategory)]
        [DefaultValue(1.0f)]
        public float ScrollSensitivity { get; set; } = 1.0f;

        /// <summary>
        /// Scroll speed multiplier with mouse wheel. Can be negative to reverse scrolling.
        /// </summary>
        [DataMember]
        [Display(category: BehaviorCategory)]
        [DefaultValue(1.0f)]
        public float MouseWheelScrollSensitivity { get; set; } = 10.0f;

        /// <summary>
        /// Gets or sets the value indicating if the element should snap its scrolling to anchors.
        /// </summary>
        /// <remarks>Snapping will work only if <see cref="Content"/> implements interface <see cref="IScrollAnchorInfo"/></remarks>
        /// <userdoc>True if the element should snap its scrolling to anchors, false otherwise.</userdoc>
        [DataMember]
        [Display(category: BehaviorCategory)]
        [DefaultValue(false)]
        public bool SnapToAnchors { get; set; } = false;

        private Vector3 lastFrameTranslation;

        private Vector3 accumulatedTranslation;

        private readonly bool[] startedSnapping = new bool[3];

        /// <summary>
        /// The current content casted as <see cref="IScrollInfo"/>.
        /// </summary>
        /// <remarks><value>Null</value> if the <see cref="Content"/> does not implement the interface</remarks>
        protected IScrollInfo ContentAsScrollInfo { get; private set; }

        /// <summary>
        /// The current content casted as <see cref="IScrollAnchorInfo"/>
        /// </summary>
        /// <remarks><value>Null</value> if the <see cref="Content"/> does not implement the interface</remarks>
        protected IScrollAnchorInfo ContentAsAnchorInfo { get; private set; }

        /// <summary>
        /// The current scroll position (in virtual pixels) of the <see cref="ScrollViewer"/>.
        /// That is, the position of the left-top-front corner of the <see cref="ScrollViewer"/> in its <see cref="Content"/>.
        /// </summary>
        /// <remarks>
        /// <para>If the <see cref="Content"/> of the scroll viewer implements the <see cref="IScrollInfo"/> interface,
        /// the <see cref="ScrollPosition"/> will be <value>0</value> in all directions where <see cref="IScrollInfo.CanScroll"/> is true.</para>
        /// <para>Note that <see cref="ScrollPosition"/> is valid only when <see cref="UIElement.IsArrangeValid"/> is <value>true</value>.
        /// If <see cref="UIElement.IsArrangeValid"/> is <value>false</value>, <see cref="ScrollPosition"/> contains the position of the scrolling
        /// before the action that actually invalidated the layout.</para>
        /// </remarks>
        public Vector3 ScrollPosition => -ScrollOffsets;

        private readonly ScrollBar[] scrollBars =
        {
            new ScrollBar { Name = "Left/Right scroll bar" },
            new ScrollBar { Name = "Top/Bottom scroll bar" },
            new ScrollBar { Name = "Back/Front scroll bar" }
        };

        private struct ScrollRequest
        {
            public Vector3 ScrollValue;

            public bool IsRelative;
        }

        /// <summary>
        /// The list of scrolling requests that need to be performed during the next <see cref="ArrangeOverride"/>
        /// </summary>
        private readonly List<ScrollRequest> scrollingRequests = new List<ScrollRequest>();

        public ScrollViewer()
        {
            // put the scroll bars above the presenter and add them to the grid canvas
            foreach (var bar in scrollBars)
            {
                bar.Measure(Vector3.Zero); // set is measure valid to true
                VisualChildrenCollection.Add(bar);
                SetVisualParent(bar, this);
            }

            CanBeHitByUser = TouchScrollingEnabled;     // Warning: this must also match in ScrollViewerMetadata
            ClipToBounds = true;                        // Warning: this must also match in ScrollViewerMetadata
        }

        /// <summary>
        /// Stops the scrolling at the current position.
        /// </summary>
        public void StopCurrentScrolling()
        {
            CurrentScrollingSpeed = Vector3.Zero;
        }

        /// <summary>
        /// Indicate if the scroll viewer can scroll in the given direction.
        /// </summary>
        /// <param name="direction">The direction to use for the test</param>
        /// <returns><value>true</value> if the scroll viewer can scroll in the provided direction, or else <value>false</value></returns>
        public bool CanScroll(Orientation direction)
        {
            return OrientationToSupportedScrollingModes[(int)direction].Contains(ScrollMode);
        }

        private class ScrollBarSorter : Comparer<UIElement>
        {
            public override int Compare(UIElement x, UIElement y)
            {
                if (x == y)
                    return 0;

                if (x == null)
                    return 1;

                if (y == null)
                    return -1;

                var xVal = x is ScrollBar ? 1 : 0;
                var yVal = y is ScrollBar ? 1 : 0;

                return xVal - yVal;
            }
        }
        private static readonly ScrollBarSorter scrollBarSorter = new ScrollBarSorter();

        private bool userManuallyScrolled;
        private ScrollingMode scrollMode = ScrollingMode.Horizontal;
        private bool touchScrollingEnabled = true;
        private float deceleration = 1500.0f;

        public override UIElement Content
        {
            get { return base.Content; }
            set
            {
                if (base.Content == value)
                    return;

                // reset scrolling owner of previous object
                if (ContentAsScrollInfo != null)
                    ContentAsScrollInfo.ScrollOwner = null;
                if (ContentAsAnchorInfo != null)
                    ContentAsAnchorInfo.ScrollOwner = null;

                base.Content = value;

                // reset the current scrolling cache data
                HideScrollBars();
                StopCurrentScrolling();
                ScrollOffsets = Vector3.Zero;

                // set content scrolling informations
                ContentAsScrollInfo = value as IScrollInfo;
                if (ContentAsScrollInfo != null)
                    ContentAsScrollInfo.ScrollOwner = this;

                // set content anchor in information
                ContentAsAnchorInfo = value as IScrollAnchorInfo;
                if (ContentAsAnchorInfo != null)
                    ContentAsAnchorInfo.ScrollOwner = this;

                VisualChildrenCollection.Sort(scrollBarSorter);
            }
        }

        protected internal void HideScrollBars()
        {
            SetScrollBarsColor(ref transparent);
        }

        private void SetScrollBarsColor(ref Color color)
        {
            foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
                scrollBars[index].BarColor = color;
        }

        /// <summary>
        /// Method triggered when <see cref="ScrollMode"/> changed.
        /// Can be overridden in inherited class to change the default behavior.
        /// </summary>
        protected virtual void OnScrollModeChanged()
        {
            ScrollOffsets = Vector3.Zero;

            if (ContentAsScrollInfo != null)
            {
                ContentAsScrollInfo.ScrollToBeginning(Orientation.Horizontal);
                ContentAsScrollInfo.ScrollToBeginning(Orientation.Vertical);
                ContentAsScrollInfo.ScrollToBeginning(Orientation.InDepth);
            }

            InvalidateMeasure();
        }

        /// <summary>
        /// Method triggered when <see cref="TouchScrollingEnabled"/> changed.
        /// Can be overridden in inherited class to change the default behavior.
        /// </summary>
        protected virtual void OnTouchScrollingEnabledChanged()
        {
            CanBeHitByUser = TouchScrollingEnabled;
        }

        /// <summary>
        /// Gets the scrolling translation that occurred during the last frame
        /// </summary>
        protected Vector3 LastFrameTranslation => lastFrameTranslation;

        /// <summary>
        /// Gets a value that indicates whether the is currently touched down.
        /// </summary>
        [DataMemberIgnore]
        protected virtual bool IsTouchedDown { get; set; }

        protected override void Update(GameTime time)
        {
            base.Update(time);

            if (!IsEnabled)
                return;

            var elapsedSeconds = (float)time.Elapsed.TotalSeconds;
            if (elapsedSeconds < MathUtil.ZeroTolerance)
                return;

            if (MouseWheelScrollSensitivity != 0f &&
                InputManager.instance != null &&
                MouseOverState != MouseOverState.MouseOverNone)
            {
                WheelScroll(InputManager.instance.MouseWheelDelta);
            }

            if (IsUserScrollingViewer || userManuallyScrolled) // scrolling is controlled by the user.
            {
                userManuallyScrolled = false;
                for (var i = 0; i < startedSnapping.Length; i++)
                    startedSnapping[i] = false;

                if (IsUserScrollingViewer) // compute the scrolling speed based on current translation
                    CurrentScrollingSpeed = LastFrameTranslation / elapsedSeconds;
            }
            else // scrolling is free: compute the scrolling translation based on the scrolling speed and anchors
            {
                lastFrameTranslation = elapsedSeconds * CurrentScrollingSpeed;

                if (SnapToAnchors && ContentAsAnchorInfo != null)
                {
                    for (var i = 0; i < 3; ++i)
                    {
                        if (!ContentAsAnchorInfo.ShouldAnchor((Orientation)i))
                            continue;

                        // get the distance to anchors from the current position.
                        var boundDistances = ContentAsAnchorInfo.GetSurroudingAnchorDistances((Orientation)i, -ScrollOffsets[i]);

                        // determine the closest anchor index
                        var closestAnchorIndex = Math.Abs(boundDistances.X) <= Math.Abs(boundDistances.Y) ? 0 : 1;

                        // check that the anchor can be reached
                        if (closestAnchorIndex == 1)
                        {
                            var offset = ContentAsScrollInfo != null && ContentAsScrollInfo.CanScroll((Orientation)i) ? -ContentAsScrollInfo.Offset[i] : ScrollOffsets[i];
                            var childRenderSize = VisualContent.RenderSize;
                            var childRenderSizeWithMargins = CalculateSizeWithThickness(ref childRenderSize, ref MarginInternal);
                            var childRenderSizeWithPadding = CalculateSizeWithThickness(ref childRenderSizeWithMargins, ref padding);
                            if (offset - boundDistances[1] < ViewPort[i] - childRenderSizeWithPadding[i])
                                closestAnchorIndex = 0;
                        }

                        // set the position manually to the anchor if already very close.
                        if (Math.Abs(CurrentScrollingSpeed[i]) < 5 && Math.Abs(boundDistances[closestAnchorIndex]) < 1) // very slow speed and closer than 1 virtual pixel to anchor
                        {
                            startedSnapping[i] = false;
                            CurrentScrollingSpeed[i] = 0;
                            lastFrameTranslation[i] = boundDistances[closestAnchorIndex];
                            continue;
                        }

                        var snappingSpeed = 5 * boundDistances[closestAnchorIndex];
                        if (startedSnapping[i] || Math.Abs(snappingSpeed) > Math.Abs(CurrentScrollingSpeed[i]))
                        {
                            // set the scrolling speed based on the remaining distance to the closest anchor
                            CurrentScrollingSpeed[i] = snappingSpeed;
                            lastFrameTranslation[i] = elapsedSeconds * CurrentScrollingSpeed[i];

                            startedSnapping[i] = true;
                        }
                    }
                }
            }

            // decrease the scrolling speed used for next frame
            foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
                CurrentScrollingSpeed[index] = Math.Sign(CurrentScrollingSpeed[index]) * Math.Max(0, Math.Abs(CurrentScrollingSpeed[index]) - elapsedSeconds * Deceleration);

            // update the scrolling position
            if (lastFrameTranslation != Vector3.Zero)
                ScrollOfInternal(ref lastFrameTranslation, false);

            // Smoothly hide the scroll bars if the no movements
            for (var dim = 0; dim < 3; dim++)
            {
                var shouldFadeOutScrollingBar = Math.Abs(CurrentScrollingSpeed[dim]) < MathUtil.ZeroTolerance && (!TouchScrollingEnabled || !IsUserScrollingViewer);
                if (shouldFadeOutScrollingBar)
                    for (var i = 0; i < 4; i++)
                        scrollBars[dim].BarColorInternal[i] = (byte)Math.Max(ScrollBarFadeAlpha * ScrollBarColor[i], scrollBars[dim].BarColorInternal[i] - ScrollBarColor[i] * ScrollBarHidingSpeed * elapsedSeconds);
                else
                    scrollBars[dim].BarColor = ScrollBarColor;
            }

            lastFrameTranslation = Vector3.Zero;
        }

        /// <summary>
        /// Go to the beginning of the scroll viewer's content in the provided direction.
        /// </summary>
        /// <param name="direction">The direction in which to scroll</param>
        /// <param name="stopScrolling">Indicate if the scrolling should be stopped after the scroll action.</param>
        public void ScrollToBeginning(Orientation direction, bool stopScrolling = true)
        {
            ScrollToExtremity(direction, stopScrolling, true);
        }

        /// <summary>
        /// Go to the end of the scroll viewer's content in the provided direction.
        /// </summary>
        /// <param name="direction">The direction in which to scroll</param>
        /// <param name="stopScrolling">Indicate if the scrolling should be stopped after the scroll action.</param>
        public void ScrollToEnd(Orientation direction, bool stopScrolling = true)
        {
            ScrollToExtremity(direction, stopScrolling, false);
        }

        private void ScrollToExtremity(Orientation direction, bool stopScrolling, bool isBeginning)
        {
            if (stopScrolling)
            {
                HideScrollBars();
                StopCurrentScrolling();
            }

            userManuallyScrolled = true;

            if (!CanScroll(direction))
                return;

            if (ContentAsScrollInfo != null && ContentAsScrollInfo.CanScroll(direction)) // scrolling is delegated to Content
            {
                if (isBeginning)
                    ContentAsScrollInfo.ScrollToBeginning(direction);
                else
                    ContentAsScrollInfo.ScrollToEnd(direction);
            }
            else // scrolling should be performed by the scroll viewer
            {
                var translation = Vector3.Zero;
                translation[(int)direction] = isBeginning ? float.NegativeInfinity : float.PositiveInfinity;

                ScrollOf(translation, stopScrolling);
            }
        }

        /// <summary>
        /// Try to scroll to the provided position (in virtual pixels).
        /// If the provided translation is too important, it is clamped.
        /// </summary>
        /// <remarks>Note that the computational cost of <see cref="ScrollTo"/> can be greatly higher than <see cref="ScrollOf"/>
        /// when scrolling is delegated to a <see cref="Content"/> virtualizing its items. When possible, prefer call to <see cref="ScrollOf"/></remarks>
        /// <param name="scrollAbsolutePosition">The scroll offsets to apply</param>
        /// <param name="stopScrolling">Indicate if the scrolling should be stopped after the scroll action.</param>
        public void ScrollTo(Vector3 scrollAbsolutePosition, bool stopScrolling = true)
        {
            if (stopScrolling)
            {
                HideScrollBars();
                StopCurrentScrolling();
            }

            userManuallyScrolled = true;

            if (VisualContent == null)
                return;

            // ask the content to internally scroll
            if (ContentAsScrollInfo != null)
            {
                var correctedScrollPosition = Vector3.Zero;
                foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
                    correctedScrollPosition[index] = scrollAbsolutePosition[index];

                // reset content scroll position to the beginning of the document and then scroll of the absolute position
                ContentAsScrollInfo.ScrollToBeginning(Orientation.Horizontal);
                ContentAsScrollInfo.ScrollToBeginning(Orientation.Vertical);
                ContentAsScrollInfo.ScrollToBeginning(Orientation.InDepth);
                ContentAsScrollInfo.ScrollOf(correctedScrollPosition);
            }

            if (IsArrangeValid) // the children size informations are still valid -> perform scrolling right away
            {
                UpdateScrollOffsets(-scrollAbsolutePosition);

                UpdateVisualContentArrangeMatrix();
            }
            else // children may have changed of size -> delay scrolling to next draw call
            {
                InvalidateArrange(); // force next arrange to perform scrolls
                scrollingRequests.Clear(); // optimization remove previous request when provided position is absolute
                scrollingRequests.Add(new ScrollRequest { ScrollValue = scrollAbsolutePosition });
            }
        }

        /// <summary>
        /// Try to scroll of the provided scrolling translation value from the current position.
        /// If the provided translation is too important, it is clamped.
        /// </summary>
        /// <param name="scrollTranslation">The scroll translation to perform (in virtual pixels)</param>
        /// <param name="stopScrolling">Indicate if the scrolling should be stopped after the scroll action.</param>
        public void ScrollOf(Vector3 scrollTranslation, bool stopScrolling = true)
        {
            userManuallyScrolled = true;

            ScrollOfInternal(ref scrollTranslation, stopScrolling);
        }

        public void ScrollOfInternal(ref Vector3 scrollTranslation, bool stopScrolling)
        {
            if (stopScrolling)
            {
                HideScrollBars();
                StopCurrentScrolling();
            }

            if (VisualContent == null)
                return;

            var correctedScrollTranslation = Vector3.Zero;
            foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
                correctedScrollTranslation[index] = scrollTranslation[index];

            // ask the content to internally scroll
            ContentAsScrollInfo?.ScrollOf(correctedScrollTranslation);

            if (IsArrangeValid) // the children size informations are still valid -> perform scrolling right away
            {
                UpdateScrollOffsets(ScrollOffsets - scrollTranslation);

                UpdateVisualContentArrangeMatrix();
            }
            else // children may have changed of size -> delay scrolling to next draw call
            {
                InvalidateArrange(); // force next arrange to perform scrolls
                scrollingRequests.Add(new ScrollRequest { IsRelative = true, ScrollValue = scrollTranslation });
            }
        }

        private void UpdateScrollOffsets(Vector3 desiredScrollPosition)
        {
            // the space desired by the child + the padding of the viewer
            Vector3 childRenderSize = VisualContent.RenderSize;
            childRenderSize.X += padding.Left + padding.Right;
            childRenderSize.Y += padding.Top + padding.Bottom;
            childRenderSize.Z += padding.Front + padding.Back;

            // update scroll viewer scroll offsets
            foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
            {
                // reset scroll offsets if scrolling is delegated to content
                if (ContentAsScrollInfo != null && ContentAsScrollInfo.CanScroll((Orientation)index))
                {
                    ScrollOffsets[index] = 0;
                    continue;
                }

                // update the scroll offset
                ScrollOffsets[index] = desiredScrollPosition[index];

                // no blank on the other extremity (reached the offset "right" limit)
                var minimumOffset = ViewPort[index] - childRenderSize[index];
                if (ScrollOffsets[index] < minimumOffset)
                {
                    ScrollOffsets[index] = minimumOffset;
                    CurrentScrollingSpeed[index] = 0;
                }

                // no positive offsets (reached the offset "left" limit)
                if (ScrollOffsets[index] > 0)
                {
                    ScrollOffsets[index] = 0;
                    CurrentScrollingSpeed[index] = 0;
                }
            }
        }

        public override bool IsEnabled
        {
            set
            {
                if (!value)
                    HideScrollBars();

                base.IsEnabled = value;
            }
        }

        protected override Vector3 MeasureOverride(Vector3 availableSizeWithoutMargins)
        {
            // measure size desired by the children
            var childDesiredSizeWithMargins = Vector3.Zero;
            if (VisualContent != null)
            {
                // remove space for padding in availableSizeWithoutMargins
                var childAvailableSizeWithMargins = CalculateSizeWithoutThickness(ref availableSizeWithoutMargins, ref padding);

                // if the content is not scrollable perform space virtualization from the scroll viewer side.
                foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
                {
                    if (ContentAsScrollInfo != null && ContentAsScrollInfo.CanScroll((Orientation)index))
                        continue;

                    childAvailableSizeWithMargins[index] = float.PositiveInfinity;
                }

                VisualContent.Measure(childAvailableSizeWithMargins);
                childDesiredSizeWithMargins = VisualContent.DesiredSizeWithMargins;
            }

            // add the padding to the child desired size
            var desiredSizeWithPadding = CalculateSizeWithThickness(ref childDesiredSizeWithMargins, ref padding);

            return desiredSizeWithPadding;
        }

        protected override Vector3 ArrangeOverride(Vector3 finalSizeWithoutMargins)
        {
            // calculate the remaining space for the child after having removed the padding space.
            ViewPort = finalSizeWithoutMargins;

            // arrange the content
            if (VisualContent != null)
            {
                // calculate the final size given to the child (scroll view virtual size)
                var childSizeWithoutPadding = CalculateSizeWithoutThickness(ref finalSizeWithoutMargins, ref padding);
                foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
                {
                    if (ContentAsScrollInfo == null || !ContentAsScrollInfo.CanScroll((Orientation)index))
                        childSizeWithoutPadding[index] = Math.Max(VisualContent.DesiredSizeWithMargins[index], childSizeWithoutPadding[index]);
                }

                // arrange the child
                VisualContent.Arrange(childSizeWithoutPadding, IsCollapsed);

                // update the scrolling bars
                UpdateScrollingBarsSize();

                // update the scrolling
                if (scrollingRequests.Count > 0)
                {
                    // perform the scrolling requests
                    foreach (var request in scrollingRequests)
                    {
                        var scrollPosition = request.IsRelative ? ScrollOffsets - request.ScrollValue : -request.ScrollValue;
                        UpdateScrollOffsets(scrollPosition);
                    }
                }
                else
                {
                    UpdateScrollOffsets(ScrollOffsets); // insures that scrolling is not out of bounds
                }

                // update the position of the child
                UpdateVisualContentArrangeMatrix();
            }

            scrollingRequests.Clear();

            return finalSizeWithoutMargins;
        }

        private void UpdateScrollingBarsSize()
        {
            // reset the bar sizes
            foreach (var scrollBar in scrollBars)
                scrollBar.Arrange(Vector3.Zero, false);

            // set the size of the bar we want to show
            foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
            {
                var sizeChildren = (ContentAsScrollInfo != null) ?
                    ContentAsScrollInfo.Extent[index] :
                    VisualContent.RenderSize[index] + VisualContent.MarginInternal[index] + VisualContent.MarginInternal[3 + index];

                var barLength = Math.Min(1f, ViewPort[index] / sizeChildren) * ViewPort[index];

                var barSize = Vector3.Zero;
                for (var dim = 0; dim < 3; dim++)
                    barSize[dim] = dim == index ? barLength : Math.Min(ScrollBarThickness, ViewPort[dim]);

                scrollBars[index].Arrange(barSize, IsCollapsed);
            }
        }

        private void UpdateVisualContentArrangeMatrix()
        {
            // calculate the offsets to move the element of
            var offsets = ScrollOffsets;
            for (var i = 0; i < 3; i++)
            {
                if (ContentAsScrollInfo != null && ContentAsScrollInfo.CanScroll((Orientation)i))
                    offsets[i] = ContentAsScrollInfo.Offset[i];
            }

            // compute the rendering offsets of the child element wrt the parent origin (0,0,0)
            var childOffsets = offsets + new Vector3(Padding.Left, Padding.Top, Padding.Front) - ViewPort / 2;

            // set the arrange matrix of the child.
            VisualContent.DependencyProperties.Set(ContentArrangeMatrixPropertyKey, Matrix.Translation(childOffsets));

            // force re-calculation of main element and scroll bars world matrices
            ArrangeChanged = true;
        }

        protected override void UpdateWorldMatrix(ref Matrix parentWorldMatrix, bool parentWorldChanged)
        {
            var shouldUpdateScrollBars = parentWorldChanged || ArrangeChanged || LocalMatrixChanged;

            base.UpdateWorldMatrix(ref parentWorldMatrix, parentWorldChanged);

            // set the world matrices of the scroll bars
            if (shouldUpdateScrollBars && VisualContent != null)
            {
                foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
                {
                    var scrollBar = scrollBars[index];
                    var barPosition = RenderSize / 2 - scrollBar.RenderSize;
                    var childMinusParent = VisualContent.DesiredSizeWithMargins[index] - ViewPort[index];

                    // determine the position ratio of the scroll bar in the viewport
                    var scrollBarPositionRatio = 0f;
                    if (ContentAsScrollInfo != null && ContentAsScrollInfo.CanScroll((Orientation)index))
                    {
                        scrollBarPositionRatio = -ContentAsScrollInfo.ScrollBarPositions[index];
                    }
                    else if (childMinusParent > MathUtil.ZeroTolerance)
                    {
                        scrollBarPositionRatio = ScrollOffsets[index] / childMinusParent;
                    }

                    // adjust the position of the scroll bar
                    barPosition[index] = -(RenderSize[index] / 2 + (scrollBarPositionRatio * (RenderSize[index] - scrollBar.RenderSize[index])));

                    var parentMatrix = WorldMatrix;
                    parentMatrix.TranslationVector += barPosition;

                    ((IUIElementUpdate)scrollBar).UpdateWorldMatrix(ref parentMatrix, true);
                }
            }
        }

        protected override void OnPreviewTouchDown(TouchEventArgs args)
        {
            base.OnPreviewTouchDown(args);

            StopCurrentScrolling();
            accumulatedTranslation = Vector3.Zero;
            IsTouchedDown = true;
        }

        protected override void OnPreviewTouchUp(TouchEventArgs args)
        {
            base.OnPreviewTouchUp(args);

            if (IsUserScrollingViewer)
            {
                args.Handled = true;
                RaiseLeaveTouchEventToHierarchyChildren(this, args);
            }

            IsUserScrollingViewer = false;
            IsTouchedDown = false;
        }

        protected override void OnTouchEnter(TouchEventArgs args)
        {
            base.OnTouchEnter(args);

            StopCurrentScrolling();
            accumulatedTranslation = Vector3.Zero;
        }

        protected override void OnTouchLeave(TouchEventArgs args)
        {
            base.OnTouchLeave(args);

            IsUserScrollingViewer = false;
            IsTouchedDown = false;
        }

        protected void WheelScroll(float amount)
        {
            if (amount == 0f) return;

            accumulatedTranslation.Y = -amount * MouseWheelScrollSensitivity;
            CurrentScrollingSpeed.Y = accumulatedTranslation.Y;
            lastFrameTranslation = accumulatedTranslation;
            IsUserScrollingViewer = true;
        }

        protected override void OnPreviewTouchMove(TouchEventArgs args)
        {
            base.OnPreviewTouchMove(args);

            if (ScrollMode == ScrollingMode.None || !TouchScrollingEnabled || !IsTouchedDown)
                return;

            // accumulate all the touch moves of the frame
            var translation = args.WorldTranslation * ScrollSensitivity;
            foreach (var index in ScrollModeToDirectionIndices[ScrollMode])
                lastFrameTranslation[index] -= translation[index];

            accumulatedTranslation += lastFrameTranslation;
            if (!IsUserScrollingViewer && accumulatedTranslation.Length() > ScrollStartThreshold)
            {
                IsUserScrollingViewer = true;
                lastFrameTranslation = accumulatedTranslation;
            }

            if (IsUserScrollingViewer)
                args.Handled = true;
        }

        private static void RaiseLeaveTouchEventToHierarchyChildren(UIElement parent, TouchEventArgs args)
        {
            if (parent == null)
                return;

            var argsCopy = new TouchEventArgs
            {
                Action = args.Action,
                ScreenPosition = args.ScreenPosition,
                ScreenTranslation = args.ScreenTranslation,
                Timestamp = args.Timestamp
            };

            foreach (var child in parent.VisualChildrenCollection)
            {
                if (child.IsTouched)
                {
                    child.RaiseTouchLeaveEvent(argsCopy);
                    RaiseLeaveTouchEventToHierarchyChildren(child, args);
                }
            }
        }

        /// <summary>
        /// Called by an <see cref="IScrollInfo"/> interface that is attached to a <see cref="ScrollViewer"/> when the value of any scrolling property size changes.
        /// Scrolling properties include offset, extent, or viewport.
        /// </summary>
        public void InvalidateScrollInfo()
        {
            if (ContentAsScrollInfo == null)
                return;

            // reset current scrolling speed if we reached one extrema of the content
            for (var i = 0; i < 3; i++)
            {
                if (ContentAsScrollInfo.ScrollBarPositions[i] < MathUtil.ZeroTolerance || ContentAsScrollInfo.ScrollBarPositions[i] > 1 - MathUtil.ZeroTolerance)
                    CurrentScrollingSpeed[i] = 0f;
            }

            UpdateScrollingBarsSize();
            UpdateVisualContentArrangeMatrix();
        }

        /// <summary>
        /// Called by an <see cref="IScrollAnchorInfo"/> interface that attached to a <see cref="ScrollViewer"/> when the value of any anchor changed.
        /// </summary>
        public void InvalidateAnchorInfo()
        {
            // currently nothing to do here.
        }

        private class ScrollViewerMetadata
        {
            [DefaultValue(true)]
            public bool CanBeHitByUser { get; }

            [DefaultValue(true)]
            public bool ClipToBounds { get; }
        }
    }
}
