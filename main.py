import argparse
import threading
import time
import ctypes

import cv2
import mss
import numpy as np
import pyautogui
from pynput import keyboard


def clamp(value, low, high):
    return max(low, min(high, value))


def load_image_bgr(path):
    image = cv2.imread(path, cv2.IMREAD_COLOR)
    if image is None:
        raise FileNotFoundError(f"Failed to load image: {path}")
    return image


def largest_contour(contours):
    if not contours:
        return None
    return max(contours, key=cv2.contourArea)


def is_square(contour, min_area, max_area):
    area = cv2.contourArea(contour)
    if area < min_area or area > max_area:
        return False
    peri = cv2.arcLength(contour, True)
    approx = cv2.approxPolyDP(contour, 0.04 * peri, True)
    if len(approx) != 4:
        return False
    x, y, w, h = cv2.boundingRect(approx)
    if h == 0:
        return False
    ratio = w / float(h)
    return 0.85 <= ratio <= 1.15


def contour_border_mask(contour, shape, border_px):
    mask = np.zeros(shape[:2], dtype=np.uint8)
    cv2.drawContours(mask, [contour], -1, 255, thickness=-1)
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (border_px, border_px))
    inner = cv2.erode(mask, kernel)
    border = cv2.subtract(mask, inner)
    return border


def hsv_ranges_from_border(bgr_image, border_px=3, std_mul=2.5, tol=(8, 30, 30)):
    # 用模板图最大轮廓的边框像素估计 HSV 颜色范围
    gray = cv2.cvtColor(bgr_image, cv2.COLOR_BGR2GRAY)
    edges = cv2.Canny(gray, 50, 150)
    contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    contour = largest_contour(contours)
    if contour is None:
        raise ValueError("No contour found in template.")

    border_mask = contour_border_mask(contour, bgr_image.shape, border_px)
    hsv = cv2.cvtColor(bgr_image, cv2.COLOR_BGR2HSV)
    pixels = hsv[border_mask > 0]
    if pixels.size == 0:
        raise ValueError("No border pixels found in template.")

    mean = pixels.mean(axis=0)
    std = pixels.std(axis=0)

    lower = mean - std_mul * std - np.array(tol)
    upper = mean + std_mul * std + np.array(tol)

    h_low = int(lower[0])
    h_high = int(upper[0])
    s_low = int(lower[1])
    s_high = int(upper[1])
    v_low = int(lower[2])
    v_high = int(upper[2])

    s_low = clamp(s_low, 0, 255)
    s_high = clamp(s_high, 0, 255)
    v_low = clamp(v_low, 0, 255)
    v_high = clamp(v_high, 0, 255)

    if h_low < 0 or h_high > 179:
        ranges = []
        if h_low < 0:
            ranges.append(((0, s_low, v_low), (h_high, s_high, v_high)))
            ranges.append(((180 + h_low, s_low, v_low), (179, s_high, v_high)))
        else:
            ranges.append(((0, s_low, v_low), (h_high - 180, s_high, v_high)))
            ranges.append(((h_low, s_low, v_low), (179, s_high, v_high)))
        return ranges

    return [((clamp(h_low, 0, 179), s_low, v_low), (clamp(h_high, 0, 179), s_high, v_high))]


def hsv_match_ratio(hsv_roi, mask, ranges):
    total = np.count_nonzero(mask)
    if total == 0:
        return 0.0
    match = 0
    for (low, high) in ranges:
        in_range = cv2.inRange(hsv_roi, np.array(low), np.array(high))
        match += np.count_nonzero(cv2.bitwise_and(in_range, in_range, mask=mask))
    return match / float(total)


def detect_squares(frame_bgr, min_area, max_area):
    gray = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY)
    blur = cv2.GaussianBlur(gray, (3, 3), 0)
    edges = cv2.Canny(blur, 40, 120)
    contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    # 仅保留近似正方形且面积在范围内的轮廓
    candidates = [c for c in contours if is_square(c, min_area, max_area)]
    return candidates


def read_center_color(frame_bgr, contour, offset_x=0, offset_y=0, sample_radius=3):
    x, y, w, h = cv2.boundingRect(contour)
    cx = x + w // 2
    cy = y + h // 2
    h_img, w_img = frame_bgr.shape[:2]
    cx = clamp(cx, 0, w_img - 1)
    cy = clamp(cy, 0, h_img - 1)
    if sample_radius <= 0:
        bgr = frame_bgr[cy, cx].tolist()
        return (cx + offset_x, cy + offset_y), bgr
    rx = max(x, cx - sample_radius)
    ry = max(y, cy - sample_radius)
    rx2 = min(x + w, cx + sample_radius + 1)
    ry2 = min(y + h, cy + sample_radius + 1)
    roi = frame_bgr[ry:ry2, rx:rx2]
    if roi.size == 0:
        bgr = frame_bgr[cy, cx].tolist()
    else:
        bgr = [int(v) for v in roi.mean(axis=(0, 1)).tolist()]
    return (cx + offset_x, cy + offset_y), bgr


def grab_region(mss_instance, center_x, center_y, size):
    # 以鼠标为中心截取固定大小的截图区域
    half = size // 2
    left = max(center_x - half, 0)
    top = max(center_y - half, 0)
    width = size
    height = size
    monitor = {"left": left, "top": top, "width": width, "height": height}
    frame = np.array(mss_instance.grab(monitor))
    return cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR), left, top


def grab_region_within(frame, offset_x, offset_y, center_x, center_y, size):
    half = size // 2
    left = clamp(center_x - half, 0, frame.shape[1] - 1)
    top = clamp(center_y - half, 0, frame.shape[0] - 1)
    right = clamp(center_x + half, 0, frame.shape[1])
    bottom = clamp(center_y + half, 0, frame.shape[0])
    roi = frame[top:bottom, left:right]
    return roi, left + offset_x, top + offset_y, left, top


def outer_frame_mask(h, w, thickness_ratio):
    # 按比例生成外框区域掩膜，排除中心区域
    t = frame_thickness(h, w, thickness_ratio)
    mask = np.zeros((h, w), dtype=np.uint8)
    mask[:t, :] = 255
    mask[-t:, :] = 255
    mask[:, :t] = 255
    mask[:, -t:] = 255
    if h > 2 * t and w > 2 * t:
        mask[t : h - t, t : w - t] = 0
    return mask


def frame_thickness(h, w, thickness_ratio):
    t = int(round(min(h, w) * thickness_ratio))
    return max(t, 1)


def masked_gray_mean(gray, mask):
    values = gray[mask > 0]
    if values.size == 0:
        return 0.0
    return float(values.mean())


def masked_bgr_mean(bgr, mask):
    values = bgr[mask > 0]
    if values.size == 0:
        return (0.0, 0.0, 0.0)
    mean = values.mean(axis=0)
    return (float(mean[0]), float(mean[1]), float(mean[2]))


def set_window_topmost_no_activate(title):
    # 置顶但不抢焦点（Windows）
    try:
        hwnd = ctypes.windll.user32.FindWindowW(None, title)
        if hwnd == 0:
            return
        SWP_NOSIZE = 0x0001
        SWP_NOMOVE = 0x0002
        SWP_NOACTIVATE = 0x0010
        HWND_TOPMOST = -1
        ctypes.windll.user32.SetWindowPos(
            hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE
        )
    except Exception:
        pass


def active_window_title():
    try:
        title = pyautogui.getActiveWindowTitle()
        return title if title else "(unknown)"
    except Exception:
        return "(unknown)"


def build_binary_mask(gray, fill_holes=False, low_contrast_std=4.0):
    blur = cv2.GaussianBlur(gray, (3, 3), 0)
    if blur.std() < low_contrast_std:
        return np.zeros_like(blur)
    norm = cv2.normalize(blur, None, 0, 255, cv2.NORM_MINMAX)
    _, otsu = cv2.threshold(norm, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    adaptive = cv2.adaptiveThreshold(
        norm, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, 31, 1
    )
    combined = cv2.bitwise_or(otsu, adaptive)
    if np.count_nonzero(combined) < combined.size * 0.01:
        adaptive_lo = cv2.adaptiveThreshold(
            norm, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, 41, 0
        )
        combined = cv2.bitwise_or(combined, adaptive_lo)
    if np.count_nonzero(combined) > (combined.size / 2):
        combined = cv2.bitwise_not(combined)
    white_ratio = np.count_nonzero(combined) / float(combined.size)
    if white_ratio > 0.85:
        strict = cv2.bitwise_and(otsu, adaptive)
        if np.count_nonzero(strict) > (strict.size / 2):
            strict = cv2.bitwise_not(strict)
        combined = strict
    kernel = np.ones((3, 3), dtype=np.uint8)
    combined = cv2.morphologyEx(combined, cv2.MORPH_CLOSE, kernel, iterations=2)
    combined = cv2.morphologyEx(combined, cv2.MORPH_OPEN, kernel, iterations=1)
    if fill_holes:
        h, w = combined.shape
        flood = combined.copy()
        mask = np.zeros((h + 2, w + 2), np.uint8)
        cv2.floodFill(flood, mask, (0, 0), 255)
        flood_inv = cv2.bitwise_not(flood)
        combined = cv2.bitwise_or(combined, flood_inv)
    return combined


def extract_shape_contour(gray):
    # 从形状模板中提取最大外轮廓
    thresh = build_binary_mask(gray)
    contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    return largest_contour(contours)


def find_frame_contours(gray, clahe_clip, clahe_tile, low_contrast_std):
    # Use thresholded, filled contours for more stable shape matching.
    clahe = cv2.createCLAHE(clipLimit=clahe_clip, tileGridSize=(clahe_tile, clahe_tile))
    enhanced = clahe.apply(gray)
    thresh = build_binary_mask(
        enhanced, fill_holes=True, low_contrast_std=low_contrast_std
    )
    contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    return contours, thresh


def contour_mask_diff_ratio(contour, template_mask):
    x, y, w, h = cv2.boundingRect(contour)
    if w <= 0 or h <= 0:
        return None, None
    mask = np.zeros((h, w), dtype=np.uint8)
    shifted = contour - np.array([[x, y]], dtype=contour.dtype)
    cv2.drawContours(mask, [shifted], -1, 255, thickness=-1)
    resized = cv2.resize(
        mask,
        (template_mask.shape[1], template_mask.shape[0]),
        interpolation=cv2.INTER_NEAREST,
    )
    diff = cv2.bitwise_xor(resized, template_mask)
    diff_ratio = np.count_nonzero(diff) / float(template_mask.size)
    fill_ratio = np.count_nonzero(resized) / float(resized.size)
    return diff_ratio, fill_ratio



class RuntimeState:
    def __init__(self):
        self.lock = threading.Lock()
        self.recorded_bgr = None
        self.last_detected_bgr = None
        self.last_detected_center = None
        self.last_detected_time = 0.0
        self.action_enabled = False

    def set_last_detected(self, bgr, center):
        with self.lock:
            self.last_detected_bgr = tuple(bgr)
            self.last_detected_center = center
            self.last_detected_time = time.time()

    def record_current(self):
        with self.lock:
            if self.last_detected_bgr is None:
                return None
            self.recorded_bgr = self.last_detected_bgr
            return self.recorded_bgr

    def toggle_action(self):
        with self.lock:
            self.action_enabled = not self.action_enabled
            return self.action_enabled

    def snapshot(self):
        with self.lock:
            return (
                self.last_detected_bgr,
                self.recorded_bgr,
                self.action_enabled,
                self.last_detected_center,
                self.last_detected_time,
            )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--shape", default="shape.png")
    parser.add_argument("--interval-ms", type=int, default=10)
    parser.add_argument("--size", type=int, default=400)
    parser.add_argument("--min-area", type=int, default=400)
    parser.add_argument("--max-area", type=int, default=100000)
    parser.add_argument("--shape-score-max", type=float, default=0.8)
    parser.add_argument("--clahe-clip", type=float, default=2.0)
    parser.add_argument("--clahe-tile", type=int, default=4)
    parser.add_argument("--scan-size", type=int, default=400)
    parser.add_argument("--track-size", type=int, default=400)
    parser.add_argument("--no-track", action="store_true")
    parser.add_argument("--no-fallback-scan", dest="fallback_scan", action="store_false")
    parser.add_argument("--select-radius", type=int, default=4)
    parser.add_argument("--debug", action="store_true")
    parser.add_argument("--debug-every", type=float, default=1.0)
    parser.add_argument("--show", action="store_true")
    parser.add_argument("--show-color", action="store_true")
    parser.add_argument("--no-show-color", dest="show_color", action="store_false")
    parser.add_argument("--color-tol", type=int, default=16)
    parser.add_argument("--action-cooldown-ms", type=int, default=50)
    parser.add_argument("--match-streak", type=int, default=2)
    parser.add_argument("--stable-px", type=int, default=2)
    parser.add_argument("--detect-fresh-ms", type=int, default=25)
    parser.add_argument("--sample-radius", type=int, default=5)
    parser.add_argument("--fill-ratio-tol", type=float, default=1.00)
    parser.add_argument("--low-contrast-std", type=float, default=4.0)
    parser.add_argument("--strict-select", action="store_true")
    parser.set_defaults(fallback_scan=True)
    parser.set_defaults(show_color=True)
    args = parser.parse_args()

    shape_img = load_image_bgr(args.shape)
    shape_gray = cv2.cvtColor(shape_img, cv2.COLOR_BGR2GRAY)
    template_mask = build_binary_mask(shape_gray, low_contrast_std=0.0)
    template_fill_ratio = np.count_nonzero(template_mask) / float(template_mask.size)
    # 用形状模板定位，用外框颜色均值区分 ing/ed
    template_contour = extract_shape_contour(shape_gray)
    if template_contour is None:
        raise ValueError("未能从 shape.png 中提取轮廓，请检查模板是否清晰。")

    state = RuntimeState()
    kb = keyboard.Controller()

    def on_press(key):
        if key == keyboard.KeyCode.from_char("x"):
            (_, _, _, center, detected_time) = state.snapshot()
            fresh = (time.time() - detected_time) * 1000 <= args.detect_fresh_ms
            if center is None:
                print("[action:x] no detected shape")
                return
            if not fresh:
                print("[action:x] stale detected shape, using last center")
            recorded = state.record_current()
            if recorded is None:
                print("[record] no detected color to record")
            else:
                print(f"[record] color={list(recorded)} center={center}")
            print(f"[action:x] press i active_window={active_window_title()}")
            kb.press("i")
            time.sleep(0.01)
            kb.release("i")
            time.sleep(0.05)
            print(f"[action:x] move to {center}")
            pyautogui.moveTo(center[0], center[1])
            time.sleep(0.05)
            print(f"[action:x] click active_window={active_window_title()}")
            pyautogui.click()
        if key == keyboard.KeyCode.from_char("c"):
            enabled = state.toggle_action()
            print(f"[action] enabled={enabled} active_window={active_window_title()}")

    listener = keyboard.Listener(on_press=on_press)
    listener.daemon = True
    listener.start()

    last_debug = time.time()
    last_selected = None
    last_selected_rect = None
    last_action_time = 0.0
    last_match = False
    color_window_init = False
    exit_requested = [False]
    exit_rect = (170, 10, 210, 40)
    match_streak = 0
    last_match_center = None
    with mss.mss() as sct:
        while True:
            # 截图并基于鼠标位置筛选方块
            pos = pyautogui.position()
            frame, offset_x, offset_y = grab_region(sct, pos.x, pos.y, args.size)
            mouse_x = pos.x - offset_x
            mouse_y = pos.y - offset_y

            def process_roi(roi_frame, roi_x, roi_y):
                if roi_frame.size == 0:
                    return None, None, 0, None
                roi_gray = cv2.cvtColor(roi_frame, cv2.COLOR_BGR2GRAY)
                contours, debug_thresh = find_frame_contours(
                    roi_gray,
                    args.clahe_clip,
                    args.clahe_tile,
                    args.low_contrast_std,
                )
                candidates = 0
                best_score = None
                selected = None
                best_dist = None
                for contour in contours:
                    area = cv2.contourArea(contour)
                    if area < args.min_area or area > args.max_area:
                        continue
                    candidates += 1
                    dist = cv2.pointPolygonTest(
                        contour, (mouse_x - roi_x, mouse_y - roi_y), True
                    )
                    if args.strict_select:
                        hit = dist >= 0
                        dist_abs = 0.0
                    else:
                        dist_abs = abs(dist)
                        hit = dist_abs <= args.select_radius
                    if hit:
                        score, fill_ratio = contour_mask_diff_ratio(contour, template_mask)
                        if score is None:
                            continue
                        if abs(fill_ratio - template_fill_ratio) > args.fill_ratio_tol:
                            continue
                        if best_score is None or score < best_score or (
                            best_score is not None and score == best_score and dist_abs < best_dist
                        ):
                            best_score = score
                            selected = contour
                            best_dist = dist_abs
                return selected, best_score, candidates, debug_thresh

            candidates = 0
            best_score = None
            selected = None
            roi_x = 0
            roi_y = 0
            mode = "scan"
            debug_thresh = None

            if not args.no_track and last_selected_rect is not None:
                rx, ry, rw, rh = last_selected_rect
                cx = rx + rw // 2
                cy = ry + rh // 2
                roi_frame, _, _, roi_x, roi_y = grab_region_within(
                    frame, offset_x, offset_y, cx, cy, args.track_size
                )
                selected, best_score, candidates, debug_thresh = process_roi(roi_frame, roi_x, roi_y)
                mode = "track"

                if (
                    args.fallback_scan
                    and (selected is None or best_score is None or best_score > args.shape_score_max)
                ):
                    selected = None
                    best_score = None
                    candidates = 0
                    roi_frame, _, _, roi_x, roi_y = grab_region_within(
                        frame, offset_x, offset_y, mouse_x, mouse_y, args.scan_size
                    )
                    selected, best_score, candidates, debug_thresh = process_roi(
                        roi_frame, roi_x, roi_y
                    )
                    mode = "scan"
            else:
                roi_frame, _, _, roi_x, roi_y = grab_region_within(
                    frame, offset_x, offset_y, mouse_x, mouse_y, args.scan_size
                )
                selected, best_score, candidates, debug_thresh = process_roi(
                    roi_frame, roi_x, roi_y
                )

            if selected is not None and best_score is not None and best_score <= args.shape_score_max:
                x, y, w, h = cv2.boundingRect(selected)
                x += roi_x
                y += roi_y
                roi = frame[y : y + h, x : x + w]
                contour = np.array(
                    [[[x, y]], [[x + w, y]], [[x + w, y + h]], [[x, y + h]]],
                    dtype=np.int32,
                )
                (cx, cy), bgr = read_center_color(
                    frame, contour, offset_x, offset_y, args.sample_radius
                )
                state.set_last_detected(bgr, (cx, cy))
                key = (cx, cy)
                if key != last_selected:
                    print(f"[shape] center=({cx},{cy}) bgr={bgr}")
                    last_selected = key
                    last_selected_rect = (x, y, w, h)
                if args.show:
                    cv2.rectangle(frame, (x, y), (x + w, y + h), (0, 255, 0), 2)
            else:
                last_selected = None
                last_selected_rect = None

            if args.debug and time.time() - last_debug >= args.debug_every:
                print(
                    f"[debug] mode={mode} candidates={candidates} "
                    f"best_score={(best_score if best_score is not None else -1):.3f}"
                )
                last_debug = time.time()
            if args.debug and debug_thresh is not None:
                cv2.imshow("debug_thresh", debug_thresh)
                cv2.setWindowProperty("debug_thresh", cv2.WND_PROP_TOPMOST, 1)
                set_window_topmost_no_activate("debug_thresh")

            (
                last_detected_bgr,
                recorded_bgr,
                action_enabled,
                last_detected_center,
                last_detected_time,
            ) = state.snapshot()
            if last_detected_bgr is not None and recorded_bgr is not None:
                diffs = [abs(a - b) for a, b in zip(last_detected_bgr, recorded_bgr)]
                match = max(diffs) <= args.color_tol
            else:
                match = False

            fresh = (time.time() - last_detected_time) * 1000 <= args.detect_fresh_ms
            if last_detected_center is None:
                match_streak = 0
                last_match_center = None
            elif match and fresh:
                if last_match_center is None:
                    match_streak = 1
                    last_match_center = last_detected_center
                else:
                    dx = last_detected_center[0] - last_match_center[0]
                    dy = last_detected_center[1] - last_match_center[1]
                    dist2 = dx * dx + dy * dy
                    if dist2 <= args.stable_px * args.stable_px:
                        match_streak += 1
                    else:
                        match_streak = 1
                        last_match_center = last_detected_center
            else:
                match_streak = 0
                last_match_center = None

            if action_enabled and match and fresh and match_streak >= args.match_streak:
                now = time.time()
                cooldown_ms = (now - last_action_time) * 1000
                if cooldown_ms >= args.action_cooldown_ms:
                    print(
                        f"[action] space cooldown_ms={cooldown_ms:.1f} "
                        f"active_window={active_window_title()}"
                    )
                    kb.press(keyboard.Key.space)
                    time.sleep(0.01)
                    kb.release(keyboard.Key.space)
                    last_action_time = now
                else:
                    print(
                        f"[action] skip cooldown_ms={cooldown_ms:.1f} "
                        f"active_window={active_window_title()}"
                    )
            last_match = match

            if args.show:
                cv2.imshow("wplace_opencv", frame)
            if args.show_color:
                panel = np.zeros((70, 220, 3), dtype=np.uint8)
                cur = np.array(last_detected_bgr or (0, 0, 0), dtype=np.uint8)
                rec = np.array(recorded_bgr or (0, 0, 0), dtype=np.uint8)
                panel[:, :80] = cur
                panel[:, 80:] = rec
                cv2.putText(panel, "CUR", (8, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
                cv2.putText(panel, "REC", (88, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
                if match:
                    cv2.putText(panel, "MATCH", (40, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)
                status = "C:ON" if action_enabled else "C:OFF"
                status_color = (0, 255, 0) if action_enabled else (0, 0, 255)
                cv2.putText(panel, status, (140, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.5, status_color, 1)
                x1, y1, x2, y2 = exit_rect
                cv2.rectangle(panel, (x1, y1), (x2, y2), (0, 0, 255), 1)
                cv2.putText(panel, "EXIT", (x1 + 6, y2 - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 0, 255), 1)
                cv2.imshow("color_status", panel)
                cv2.setWindowProperty("color_status", cv2.WND_PROP_TOPMOST, 1)
                set_window_topmost_no_activate("color_status")
                if not color_window_init:
                    def on_color_click(event, x, y, flags, param):
                        if event == cv2.EVENT_LBUTTONDOWN:
                            if x1 <= x <= x2 and y1 <= y <= y2:
                                exit_requested[0] = True
                    cv2.setMouseCallback("color_status", on_color_click)
                    color_window_init = True

            if args.show or args.show_color:
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
            if exit_requested[0]:
                break

            time.sleep(args.interval_ms / 1000.0)


if __name__ == "__main__":
    main()
