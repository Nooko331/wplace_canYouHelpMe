import argparse
import threading
import time
import ctypes

import cv2
import mss
import numpy as np
import pyautogui
from pynput import keyboard


class RuntimeState:
    def __init__(self):
        self.lock = threading.Lock()
        self.current_bgr = None
        self.current_pos = None
        self.recorded_bgr = None
        self.action_enabled = False
        self.last_action_time = 0.0

    def update_current(self, bgr, pos):
        with self.lock:
            self.current_bgr = bgr
            self.current_pos = pos

    def record_current(self):
        with self.lock:
            if self.current_bgr is None:
                return None
            self.recorded_bgr = self.current_bgr
            return self.recorded_bgr

    def toggle_action(self):
        with self.lock:
            self.action_enabled = not self.action_enabled
            return self.action_enabled

    def snapshot(self):
        with self.lock:
            return (
                self.current_bgr,
                self.current_pos,
                self.recorded_bgr,
                self.action_enabled,
                self.last_action_time,
            )

    def set_last_action_time(self, ts):
        with self.lock:
            self.last_action_time = ts


def grab_pixel(mss_instance, x, y):
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--interval-ms", type=int, default=50)
    parser.add_argument("--color-tol", type=int, default=5)
    parser.add_argument("--cooldown-ms", type=int, default=50)
    parser.add_argument("--debug", action="store_true")
    parser.add_argument("--show", action="store_true")
    args = parser.parse_args()

    state = RuntimeState()
    kb = keyboard.Controller()

    def on_press(key):
        if key == keyboard.KeyCode.from_char("x"):
            recorded = state.record_current()
            if recorded is None:
                print("[record] no current color")
            else:
                (_, pos, _, _, _) = state.snapshot()
                print(f"[record] color={list(recorded)} pos={pos}")
            kb.press("i")
            time.sleep(0.01)
            kb.release("i")
            time.sleep(0.01)
            pyautogui.click()
        if key == keyboard.KeyCode.from_char("c"):
            enabled = state.toggle_action()
            print(f"[toggle] enabled={enabled}")

    listener = keyboard.Listener(on_press=on_press)
    listener.daemon = True
    listener.start()

    last_print_bgr = None
    exit_requested = [False]
    color_window_init = False
    exit_rect = (170, 10, 210, 40)
    with mss.mss() as sct:
        while True:
            pos = pyautogui.position()
            bgr = grab_pixel(sct, pos.x, pos.y)
            state.update_current(bgr, (pos.x, pos.y))

            if args.debug and bgr != last_print_bgr:
                print(f"[color] pos=({pos.x},{pos.y}) bgr={bgr}")
                last_print_bgr = bgr

            (cur_bgr, _, rec_bgr, enabled, last_action_time) = state.snapshot()
            match = False
            if cur_bgr is not None and rec_bgr is not None:
                diffs = [abs(a - b) for a, b in zip(cur_bgr, rec_bgr)]
                match = max(diffs) <= args.color_tol

            if enabled and match:
                now = time.time()
                if (now - last_action_time) * 1000 >= args.cooldown_ms:
                    kb.press(keyboard.Key.space)
                    time.sleep(0.01)
                    kb.release(keyboard.Key.space)
                    state.set_last_action_time(now)

            if args.show:
                panel = np.zeros((70, 220, 3), dtype=np.uint8)
                cur = np.array(cur_bgr or (0, 0, 0), dtype=np.uint8)
                rec = np.array(rec_bgr or (0, 0, 0), dtype=np.uint8)
                panel[:, :80] = cur
                panel[:, 80:] = rec
                cv2.putText(
                    panel,
                    "CUR",
                    (8, 20),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (255, 255, 255),
                    1,
                )
                cv2.putText(
                    panel,
                    "REC",
                    (88, 20),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (255, 255, 255),
                    1,
                )
                if match:
                    cv2.putText(
                        panel,
                        "MATCH",
                        (40, 60),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.5,
                        (0, 255, 0),
                        1,
                    )
                status = "C:ON" if enabled else "C:OFF"
                status_color = (0, 255, 0) if enabled else (0, 0, 255)
                cv2.putText(
                    panel,
                    status,
                    (140, 60),
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

                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
            if exit_requested[0]:
                break

            time.sleep(args.interval_ms / 1000.0)


if __name__ == "__main__":
    main()
