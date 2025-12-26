import argparse
import threading
import time
import ctypes
import logging
import os

import cv2
import mss
import numpy as np
import pyautogui
from pynput import keyboard
import tkinter as tk


class RuntimeState:
    def __init__(self):
        self.lock = threading.Lock()
        self.current_bgr = None
        self.current_pos = None
        self.recorded_bgr = None
        self.recorded_bgrs = []
        self.recorded_pos = None
        self.recorded_bgrs_raw = []
        self.action_enabled = False
        self.last_action_time = 0.0
        self.recorded_range = None
        self.auto_fill_enabled = False
        self.auto_fill_points = []
        self.auto_fill_index = 0
        self.auto_fill_primed = False

    def update_current(self, bgr, pos):
        with self.lock:
            self.current_bgr = bgr
            self.current_pos = pos

    def record_current(self):
        with self.lock:
            if self.current_bgr is None:
                return None
            self.recorded_bgr = self.current_bgr
            self.recorded_bgrs = [self.current_bgr]
            self.recorded_bgrs_raw = [self.current_bgr]
            self.recorded_pos = self.current_pos
            return self.recorded_bgr

    def set_recorded(self, bgr, pos):
        with self.lock:
            self.recorded_bgr = bgr
            self.recorded_bgrs = [bgr]
            self.recorded_bgrs_raw = [bgr]
            self.recorded_pos = pos
            return self.recorded_bgr

    def set_recorded_multi(self, bgrs, pos):
        with self.lock:
            self.recorded_bgrs = list(bgrs)
            self.recorded_bgr = self.recorded_bgrs[0] if self.recorded_bgrs else None
            self.recorded_pos = pos
            return self.recorded_bgrs

    def set_recorded_multi_with_raw(self, bgrs_raw, bgrs_match, pos):
        with self.lock:
            self.recorded_bgrs = list(bgrs_match)
            self.recorded_bgr = self.recorded_bgrs[0] if self.recorded_bgrs else None
            self.recorded_pos = pos
            self.recorded_bgrs_raw = list(bgrs_raw)
            return self.recorded_bgrs

    def toggle_action(self):
        with self.lock:
            self.action_enabled = not self.action_enabled
            return self.action_enabled

    def set_range(self, rect):
        with self.lock:
            self.recorded_range = rect

    def toggle_auto_fill(self):
        with self.lock:
            self.auto_fill_enabled = not self.auto_fill_enabled
            if not self.auto_fill_enabled:
                self.auto_fill_points = []
                self.auto_fill_index = 0
                self.auto_fill_primed = False
            return self.auto_fill_enabled

    def start_auto_fill(self):
        with self.lock:
            self.auto_fill_enabled = True
            self.auto_fill_index = 0
            self.auto_fill_primed = False
            return self.auto_fill_enabled

    def stop_all(self):
        with self.lock:
            self.action_enabled = False
            self.auto_fill_enabled = False
            self.auto_fill_points = []
            self.auto_fill_index = 0
            self.auto_fill_primed = False

    def set_auto_fill_points(self, points):
        with self.lock:
            self.auto_fill_points = points
            self.auto_fill_index = 0
            self.auto_fill_primed = False

    def next_auto_fill_point(self):
        with self.lock:
            if not self.auto_fill_points:
                self.auto_fill_enabled = False
                return None
            if self.auto_fill_index >= len(self.auto_fill_points):
                self.auto_fill_enabled = False
                return None
            pt = self.auto_fill_points[self.auto_fill_index]
            self.auto_fill_index += 1
            return pt

    def is_auto_fill_primed(self):
        with self.lock:
            return self.auto_fill_primed

    def set_auto_fill_primed(self):
        with self.lock:
            self.auto_fill_primed = True

    def snapshot(self):
        with self.lock:
            return (
                self.current_bgr,
                self.current_pos,
                self.recorded_bgr,
                self.recorded_pos,
                list(self.recorded_bgrs),
                self.action_enabled,
                self.last_action_time,
                self.recorded_range,
                self.auto_fill_enabled,
                list(self.auto_fill_points),
                self.auto_fill_index,
                list(self.recorded_bgrs_raw),
            )

    def set_last_action_time(self, ts):
        with self.lock:
            self.last_action_time = ts


LOGGER = logging.getLogger("color_watch")
DEBUG_ENABLED = False


def log_debug(msg):
    if DEBUG_ENABLED:
        LOGGER.debug(msg)


def build_srgb_lut():
    vals = np.arange(256, dtype=np.float32) / 255.0
    linear = np.where(
        vals <= 0.04045,
        vals / 12.92,
        ((vals + 0.055) / 1.055) ** 2.4,
    )
    return np.round(linear * 255.0).astype(np.int16)


SRGB_LUT = build_srgb_lut()


def apply_srgb_lut(bgr):
    return (
        int(SRGB_LUT[bgr[0]]),
        int(SRGB_LUT[bgr[1]]),
        int(SRGB_LUT[bgr[2]]),
    )


def bgr_to_rgb(bgr):
    return (int(bgr[2]), int(bgr[1]), int(bgr[0]))


def set_dpi_awareness():
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)
    except Exception:
        try:
            ctypes.windll.user32.SetProcessDPIAware()
        except Exception:
            pass


def grab_pixel(mss_instance, x, y):
    monitor = {"left": int(x), "top": int(y), "width": 1, "height": 1}
    frame = mss_instance.grab(monitor)
    px = frame.pixel(0, 0)
    if len(px) == 4:
        b, g, r, _ = px
    else:
        b, g, r = px
    return (int(b), int(g), int(r))


def grab_pixel_raw(mss_instance, x, y):
    monitor = {"left": int(x), "top": int(y), "width": 1, "height": 1}
    frame = mss_instance.grab(monitor)
    px = frame.pixel(0, 0)
    if len(px) == 4:
        b, g, r, _ = px
    else:
        b, g, r = px
    return (int(b), int(g), int(r))


def set_window_topmost_no_activate(title):
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


def select_range(sct, cursor_pos=None, debug=False):
    result = {"rect": None}
    start = {"pos": None}
    rect_id = {"id": None}

    def log(msg):
        if debug:
            log_debug(msg)

    monitors = sct.monitors[1:] or [sct.monitors[0]]
    if cursor_pos is None:
        cursor_pos = pyautogui.position()
    target = monitors[0]
    for mon in monitors:
        if (
            cursor_pos.x >= mon["left"]
            and cursor_pos.x < mon["left"] + mon["width"]
            and cursor_pos.y >= mon["top"]
            and cursor_pos.y < mon["top"] + mon["height"]
        ):
            target = mon
            break

    root = tk.Tk()
    root.withdraw()
    root.geometry(
        f"{target['width']}x{target['height']}+{target['left']}+{target['top']}"
    )
    root.attributes("-topmost", True)
    root.overrideredirect(True)
    root.configure(bg="black")
    root.attributes("-alpha", 0.2)

    canvas = tk.Canvas(root, bg="black", highlightthickness=0, cursor="crosshair")
    canvas.pack(fill="both", expand=True)

    def on_press(event):
        log(f"[range] press ({event.x},{event.y})")
        start["pos"] = (event.x, event.y)
        if rect_id["id"] is not None:
            canvas.delete(rect_id["id"])
            rect_id["id"] = None

    def on_drag(event):
        # log(f"[range] drag ({event.x},{event.y})")
        if not start["pos"]:
            return
        x1, y1 = start["pos"]
        x2, y2 = event.x, event.y
        if rect_id["id"] is None:
            rect_id["id"] = canvas.create_rectangle(
                x1, y1, x2, y2, outline="red", width=4
            )
        else:
            canvas.coords(rect_id["id"], x1, y1, x2, y2)

    def on_release(event):
        log(f"[range] release ({event.x},{event.y})")
        if not start["pos"]:
            root.quit()
            return
        x1, y1 = start["pos"]
        x2, y2 = event.x, event.y
        left = min(x1, x2)
        right = max(x1, x2)
        top = min(y1, y2)
        bottom = max(y1, y2)
        if right - left < 3 or bottom - top < 3:
            log("[range] drag too small, retry")
            start["pos"] = None
            if rect_id["id"] is not None:
                canvas.delete(rect_id["id"])
                rect_id["id"] = None
            return
        result["rect"] = (
            left + target["left"],
            top + target["top"],
            right + target["left"],
            bottom + target["top"],
        )
        root.quit()

    def on_cancel(_event=None):
        log("[range] cancel")
        result["rect"] = None
        root.quit()

    root.bind("<ButtonPress-1>", on_press)
    root.bind("<B1-Motion>", on_drag)
    root.bind("<ButtonRelease-1>", on_release)
    root.bind("<Escape>", on_cancel)
    root.bind("<ButtonPress-3>", on_cancel)

    root.deiconify()
    root.lift()
    root.focus_force()
    canvas.focus_set()
    try:
        root.grab_set()
    except tk.TclError as exc:
        log(f"[range] grab_set failed: {exc}")
    log(
        f"[range] overlay ready screen=({target['left']},{target['top']},"
        f"{target['width']},{target['height']})"
    )
    root.mainloop()
    root.destroy()
    log(f"[range] overlay done rect={result['rect']}")
    return result["rect"]


def is_point_in_rect(pos, rect):
    if rect is None:
        return False
    x, y = pos
    left, top, right, bottom = rect
    return left <= x <= right and top <= y <= bottom


def pick_safe_pos(monitors, avoid_rect, padding=5):
    for mon in monitors:
        left = mon["left"]
        top = mon["top"]
        right = mon["left"] + mon["width"] - 1
        bottom = mon["top"] + mon["height"] - 1
        candidates = [
            (left + padding, top + padding),
            (right - padding, top + padding),
            (left + padding, bottom - padding),
            (right - padding, bottom - padding),
        ]
        for x, y in candidates:
            if not is_point_in_rect((x, y), avoid_rect):
                return (x, y)
    return None


def scan_matching_points(
    sct, rect, bgrs, tol, step, debug=False, ref_pos=None, probe_pos=None
):
    left, top, right, bottom = rect
    width = max(1, right - left + 1)
    height = max(1, bottom - top + 1)
    points = []
    matches_with_color = []
    diff_min_global = None
    diff_max_global = None
    diff_sum = 0
    diff_count = 0

    if debug and probe_pos is not None:
        if left <= probe_pos[0] <= right and top <= probe_pos[1] <= bottom:
            probe_bgr = grab_pixel(sct, probe_pos[0], probe_pos[1])
            diffs = []
            for rbgr in bgrs:
                diffs.append([abs(a - b) for a, b in zip(probe_bgr, rbgr)])
            log_debug(
                f"[scan] probe pos={probe_pos} "
                f"bgr_rgb={list(bgr_to_rgb(probe_bgr))} diffs={diffs}"
            )

    for y in range(0, height, step):
        for x in range(0, width, step):
            px = (left + x, top + y)
            bgr = grab_pixel(sct, px[0], px[1])
            max_diffs = []
            for rbgr in bgrs:
                diff = [abs(a - b) for a, b in zip(bgr, rbgr)]
                max_diffs.append(max(diff))
            min_diff = min(max_diffs) if max_diffs else 0
            diff_sum += min_diff
            diff_count += 1
            if diff_min_global is None or min_diff < diff_min_global:
                diff_min_global = min_diff
            if diff_max_global is None or min_diff > diff_max_global:
                diff_max_global = min_diff
            if min_diff <= tol:
                points.append(px)
                if debug:
                    matches_with_color.append((px[0], px[1], list(bgr_to_rgb(bgr))))

    if debug:
        avg_diff = (diff_sum / diff_count) if diff_count else -1
        log_debug(
            f"[scan] step_used={step} matches={len(points)} "
            f"min_diff={diff_min_global} max_diff={diff_max_global} "
            f"avg_diff={avg_diff:.2f} matches_with_color={matches_with_color}"
        )
    return points


def main():
    set_dpi_awareness()
    parser = argparse.ArgumentParser()
    parser.add_argument("--interval-ms", type=int, default=100)
    parser.add_argument("--color-tol", type=int, default=0)
    parser.add_argument("--cooldown-ms", type=int, default=20)
    parser.add_argument("--probe-x", type=int, default=None)
    parser.add_argument("--probe-y", type=int, default=None)
    parser.add_argument("--debug", action="store_true")
    parser.add_argument("--show", action="store_true")
    parser.add_argument("--no-show", dest="show", action="store_false")
    parser.set_defaults(show=True)
    args = parser.parse_args()

    global DEBUG_ENABLED
    if args.debug:
        LOGGER.setLevel(logging.DEBUG)
        formatter = logging.Formatter("%(asctime)s %(levelname)s %(message)s")
        if not LOGGER.handlers:
            file_handler = logging.FileHandler(
                "simple_color_watch.log", encoding="utf-8"
            )
            file_handler.setFormatter(formatter)
            stream_handler = logging.StreamHandler()
            stream_handler.setFormatter(formatter)
            LOGGER.addHandler(file_handler)
            LOGGER.addHandler(stream_handler)
        LOGGER.propagate = False
        DEBUG_ENABLED = True

    state = RuntimeState()
    kb = keyboard.Controller()

    def on_press(key):
        if key == keyboard.Key.esc:
            state.stop_all()
            print("[stop] action/auto_fill disabled")
            return
        if key == keyboard.KeyCode.from_char("z"):
            pos = pyautogui.position()
            with mss.mss() as sct_local:
                bgr_hover_raw = grab_pixel_raw(sct_local, pos.x, pos.y)
                safe = pick_safe_pos(
                    sct_local.monitors[1:], (pos.x, pos.y, pos.x, pos.y)
                )
                moved = False
                if safe is not None:
                    pyautogui.moveTo(safe[0], safe[1])
                    time.sleep(0.03)
                    moved = True
                bgr_clear_raw = grab_pixel_raw(sct_local, pos.x, pos.y)
                recorded = state.set_recorded_multi_with_raw(
                    [bgr_hover_raw, bgr_clear_raw],
                    [bgr_hover_raw, bgr_clear_raw],
                    (pos.x, pos.y),
                )
                if moved:
                    pyautogui.moveTo(pos.x, pos.y)
                print(
                    f"[record] raw_colors_rgb={list(bgr_to_rgb(bgr_hover_raw))},"
                    f"{list(bgr_to_rgb(bgr_clear_raw))} "
                    f"match_colors_rgb={list(bgr_to_rgb(recorded[0]))},"
                    f"{list(bgr_to_rgb(recorded[1]))} pos={(pos.x, pos.y)}"
                )
            kb.press("i")
            time.sleep(0.01)
            kb.release("i")
            time.sleep(0.01)
            pyautogui.click()
        if key == keyboard.KeyCode.from_char("x"):
            enabled = state.toggle_action()
            print(f"[toggle] enabled={enabled}")

    listener = keyboard.Listener(on_press=on_press)
    listener.daemon = True
    listener.start()

    last_print_bgr = None
    exit_requested = [False]
    color_window_init = False
    request_select_range = [False]
    exit_rect = (90, 10, 130, 40)
    range_rect = (10, 10, 80, 40)
    fill_rect = (10, 45, 80, 75)
    with mss.mss() as sct:
        while True:
            pos = pyautogui.position()
            bgr = grab_pixel(sct, pos.x, pos.y)
            state.update_current(bgr, (pos.x, pos.y))

            if args.debug and bgr != last_print_bgr:
                # log_debug(f"[color] pos=({pos.x},{pos.y}) bgr={bgr}")
                last_print_bgr = bgr

            (
                cur_bgr,
                _,
                rec_bgr,
                rec_pos,
                rec_bgrs,
                enabled,
                last_action_time,
                recorded_range,
                auto_fill_enabled,
                auto_fill_points,
                _,
                rec_bgrs_raw,
            ) = state.snapshot()
            match = False
            if cur_bgr is not None and rec_bgrs:
                for rbgr in rec_bgrs:
                    diffs = [abs(a - b) for a, b in zip(cur_bgr, rbgr)]
                    if max(diffs) <= args.color_tol:
                        match = True
                        break

            if enabled and match:
                now = time.time()
                if (now - last_action_time) * 1000 >= args.cooldown_ms:
                    kb.press(keyboard.Key.space)
                    time.sleep(0.01)
                    kb.release(keyboard.Key.space)
                    state.set_last_action_time(now)

            if auto_fill_enabled and recorded_range and rec_bgrs:
                now = time.time()
                if (now - last_action_time) * 1000 >= args.cooldown_ms:
                    pt = state.next_auto_fill_point()
                    if pt is not None:
                        if args.debug:
                            log_debug(f"[auto_fill] fire pt={pt}")
                        pyautogui.moveTo(pt[0], pt[1])
                        if not state.is_auto_fill_primed():
                            pyautogui.click()
                            state.set_auto_fill_primed()
                        kb.press(keyboard.Key.space)
                        time.sleep(0.01)
                        kb.release(keyboard.Key.space)
                        state.set_last_action_time(now)
                    elif args.debug:
                        log_debug("[auto_fill] no points to fire")
                elif args.debug:
                    remaining = args.cooldown_ms - (now - last_action_time) * 1000
                    log_debug(f"[auto_fill] cooldown remaining={remaining:.1f}ms")

            if args.show:
                panel = np.zeros((90, 140, 3), dtype=np.uint8)
                left_color = (0, 0, 0)
                right_color = (0, 0, 0)
                if rec_bgrs_raw:
                    left_color = bgr_to_rgb(rec_bgrs_raw[0])
                    if len(rec_bgrs_raw) > 1:
                        right_color = bgr_to_rgb(rec_bgrs_raw[1])
                    else:
                        right_color = bgr_to_rgb(rec_bgrs_raw[0])
                panel[:, :70] = np.array(left_color, dtype=np.uint8)
                panel[:, 70:] = np.array(right_color, dtype=np.uint8)
                cv2.putText(
                    panel,
                    "REC",
                    (10, 20),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (255, 255, 255),
                    1,
                )
                if match:
                    cv2.putText(
                        panel,
                        "MATCH",
                        (10, 88),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.5,
                        (0, 255, 0),
                        1,
                    )
                status = "X:ON" if enabled else "X:OFF"
                status_color = (0, 255, 0) if enabled else (0, 0, 255)
                cv2.putText(
                    panel,
                    status,
                    (70, 88),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    status_color,
                    1,
                )
                x1, y1, x2, y2 = exit_rect
                cv2.rectangle(panel, (x1, y1), (x2, y2), (0, 0, 255), 1)
                cv2.putText(
                    panel,
                    "EXIT",
                    (x1 + 6, y2 - 10),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.45,
                    (0, 0, 255),
                    1,
                )
                rx1, ry1, rx2, ry2 = range_rect
                cv2.rectangle(panel, (rx1, ry1), (rx2, ry2), (0, 255, 255), 1)
                cv2.putText(
                    panel,
                    "RANGE",
                    (rx1 + 4, ry2 - 10),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.4,
                    (0, 255, 255),
                    1,
                )
                fx1, fy1, fx2, fy2 = fill_rect
                fill_color = (0, 255, 0) if auto_fill_enabled else (0, 0, 255)
                cv2.rectangle(panel, (fx1, fy1), (fx2, fy2), fill_color, 1)
                cv2.putText(
                    panel,
                    "FILL",
                    (fx1 + 12, fy2 - 8),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.4,
                    fill_color,
                    1,
                )
                if recorded_range:
                    cv2.putText(
                        panel,
                        "R:OK",
                        (90, 60),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.4,
                        (255, 255, 255),
                        1,
                    )
                cv2.imshow("color_status", panel)
                cv2.setWindowProperty("color_status", cv2.WND_PROP_TOPMOST, 1)
                set_window_topmost_no_activate("color_status")
                if not color_window_init:

                    def on_color_click(event, x, y, flags, param):
                        if event == cv2.EVENT_LBUTTONDOWN:
                            if args.debug:
                                log_debug(f"[ui] click pos=({x},{y})")
                            if x1 <= x <= x2 and y1 <= y <= y2:
                                if args.debug:
                                    log_debug("[ui] exit clicked")
                                exit_requested[0] = True
                            if rx1 <= x <= rx2 and ry1 <= y <= ry2:
                                if args.debug:
                                    log_debug("[ui] range clicked")
                                request_select_range[0] = True
                            if fx1 <= x <= fx2 and fy1 <= y <= fy2:
                                enabled_fill = state.start_auto_fill()
                                if enabled_fill:
                                    (
                                        _,
                                        _,
                                        rec_bgr_now,
                                        rec_pos_now,
                                        rec_bgrs_now,
                                        _,
                                        _,
                                        recorded_range_now,
                                        _,
                                        _,
                                        _,
                                        _,
                                    ) = state.snapshot()
                                    if not recorded_range_now or not rec_bgrs_now:
                                        state.stop_all()
                                        print("[auto_fill] missing range or color")
                                    else:
                                        original_pos = pyautogui.position()
                                        moved = False
                                        if is_point_in_rect(
                                            (original_pos.x, original_pos.y),
                                            recorded_range_now,
                                        ):
                                            safe_pos = pick_safe_pos(
                                                sct.monitors[1:], recorded_range_now
                                            )
                                            if safe_pos is not None:
                                                pyautogui.moveTo(
                                                    safe_pos[0], safe_pos[1]
                                                )
                                                time.sleep(0.03)
                                                moved = True
                                        points = scan_matching_points(
                                            sct,
                                            recorded_range_now,
                                            rec_bgrs_now,
                                            args.color_tol,
                                            step=10,
                                            debug=args.debug,
                                            ref_pos=rec_pos_now,
                                            probe_pos=(
                                                (
                                                    args.probe_x,
                                                    args.probe_y,
                                                )
                                                if args.probe_x is not None
                                                and args.probe_y is not None
                                                else None
                                            ),
                                        )
                                        if moved:
                                            pyautogui.moveTo(
                                                original_pos.x, original_pos.y
                                            )
                                        state.set_auto_fill_points(points)
                                        print(
                                            f"[auto_fill] enabled points={len(points)}"
                                        )

                    cv2.setMouseCallback("color_status", on_color_click)
                    color_window_init = True

                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
            if exit_requested[0]:
                break

            if request_select_range[0]:
                request_select_range[0] = False
                print("[range] begin")
                rect = select_range(sct, cursor_pos=pos, debug=args.debug)
                if rect:
                    state.set_range(rect)
                    print(f"[range] done rect={rect}")
                else:
                    print("[range] canceled")

            time.sleep(args.interval_ms / 1000.0)


if __name__ == "__main__":
    main()
