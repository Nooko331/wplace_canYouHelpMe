import argparse
import threading
import time

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


def read_center_color(frame_bgr, contour, offset_x=0, offset_y=0):
    x, y, w, h = cv2.boundingRect(contour)
    cx = x + w // 2
    cy = y + h // 2
    h_img, w_img = frame_bgr.shape[:2]
    cx = clamp(cx, 0, w_img - 1)
    cy = clamp(cy, 0, h_img - 1)
    bgr = frame_bgr[cy, cx].tolist()
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


class ToggleState:
    def __init__(self):
        self.enabled = False
        self.lock = threading.Lock()

    def toggle(self):
        with self.lock:
            self.enabled = not self.enabled
            return self.enabled

    def is_enabled(self):
        with self.lock:
            return self.enabled


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--processing", default="ing.png")
    parser.add_argument("--done", default="ed.png")
    parser.add_argument("--interval-ms", type=int, default=100)
    parser.add_argument("--size", type=int, default=400)
    parser.add_argument("--min-area", type=int, default=200)
    parser.add_argument("--max-area", type=int, default=100000)
    parser.add_argument("--border-px", type=int, default=3)
    parser.add_argument("--match-threshold", type=float, default=0.35)
    parser.add_argument("--processing-std-mul", type=float, default=2.5)
    parser.add_argument("--done-std-mul", type=float, default=2.5)
    parser.add_argument("--processing-tol-h", type=int, default=8)
    parser.add_argument("--processing-tol-s", type=int, default=30)
    parser.add_argument("--processing-tol-v", type=int, default=30)
    parser.add_argument("--done-tol-h", type=int, default=8)
    parser.add_argument("--done-tol-s", type=int, default=30)
    parser.add_argument("--done-tol-v", type=int, default=30)
    parser.add_argument("--debug", action="store_true")
    parser.add_argument("--debug-every", type=float, default=1.0)
    parser.add_argument("--show", action="store_true")
    parser.add_argument("--dedupe-seconds", type=float, default=1.0)
    args = parser.parse_args()

    # 从用户提供的模板图生成 HSV 颜色范围
    processing_img = load_image_bgr(args.processing)
    done_img = load_image_bgr(args.done)

    processing_ranges = hsv_ranges_from_border(
        processing_img,
        border_px=args.border_px,
        std_mul=args.processing_std_mul,
        tol=(args.processing_tol_h, args.processing_tol_s, args.processing_tol_v),
    )
    done_ranges = hsv_ranges_from_border(
        done_img,
        border_px=args.border_px,
        std_mul=args.done_std_mul,
        tol=(args.done_tol_h, args.done_tol_s, args.done_tol_v),
    )

    state = ToggleState()

    def on_press(key):
        if key == keyboard.KeyCode.from_char("x"):
            enabled = state.toggle()
            print(f"[toggle] enabled={enabled}")

    listener = keyboard.Listener(on_press=on_press)
    listener.daemon = True
    listener.start()

    last_debug = time.time()
    last_cleanup = time.time()
    last_emit = {}
    last_selected = None
    with mss.mss() as sct:
        while True:
            if not state.is_enabled():
                time.sleep(0.05)
                continue

            # 截图并基于鼠标位置筛选方块
            pos = pyautogui.position()
            frame, offset_x, offset_y = grab_region(sct, pos.x, pos.y, args.size)
            mouse_x = pos.x - offset_x
            mouse_y = pos.y - offset_y

            candidates = detect_squares(frame, args.min_area, args.max_area)
            hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
            max_processing = 0.0
            max_done = 0.0
            selected = None

            for contour in candidates:
                # 优先选择鼠标所在的方块
                if cv2.pointPolygonTest(contour, (mouse_x, mouse_y), False) >= 0:
                    selected = contour
                    break
                border_mask = contour_border_mask(contour, frame.shape, args.border_px)
                processing_ratio = hsv_match_ratio(hsv, border_mask, processing_ranges)
                done_ratio = hsv_match_ratio(hsv, border_mask, done_ranges)
                max_processing = max(max_processing, processing_ratio)
                max_done = max(max_done, done_ratio)

                if args.show:
                    x, y, w, h = cv2.boundingRect(contour)
                    cv2.rectangle(frame, (x, y), (x + w, y + h), (50, 120, 255), 1)

            if selected is not None:
                border_mask = contour_border_mask(selected, frame.shape, args.border_px)
                processing_ratio = hsv_match_ratio(hsv, border_mask, processing_ranges)
                done_ratio = hsv_match_ratio(hsv, border_mask, done_ranges)

                if processing_ratio >= args.match_threshold and processing_ratio >= done_ratio:
                    (cx, cy), bgr = read_center_color(frame, selected, offset_x, offset_y)
                    key = (cx, cy)
                    if key != last_selected:
                        print(f"[processing] center=({cx},{cy}) bgr={bgr}")
                        last_selected = key
                    if args.show:
                        cv2.rectangle(
                            frame,
                            (cx - 3 - offset_x, cy - 3 - offset_y),
                            (cx + 3 - offset_x, cy + 3 - offset_y),
                            (0, 255, 0),
                            1,
                        )
                else:
                    last_selected = None

            if args.debug and time.time() - last_debug >= args.debug_every:
                print(
                    f"[debug] candidates={len(candidates)} "
                    f"max_processing={max_processing:.3f} max_done={max_done:.3f}"
                )
                last_debug = time.time()

            if args.show:
                cv2.imshow("wplace_opencv", frame)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break

            if time.time() - last_cleanup >= 10.0 and len(last_emit) > 200:
                cutoff = time.time() - args.dedupe_seconds * 3
                last_emit = {k: v for k, v in last_emit.items() if v >= cutoff}
                last_cleanup = time.time()

            time.sleep(args.interval_ms / 1000.0)


if __name__ == "__main__":
    main()
