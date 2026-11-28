/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 *
 * libmoavi-rdp against a real RDP server (CI: .github/workflows/rdp-check.yml, an xrdp container):
 * connect, frames arrive and the buffer has a picture, a key, a drive added and taken away, the clipboard,
 * the window size, a clean disconnect.
 *   rdp_check /v:127.0.0.1:3389 /u:abc /p:abc /cert:ignore ...
 */
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

void* moavi_rdp_new(int argc, char** argv, void (*frame)(void*, int, int, int, int), void (*size)(void*, int, int),
                    void (*clip)(void*, const char*), void (*state)(void*, int, const char*), void* user);
int moavi_rdp_run(void* h);
unsigned char* moavi_rdp_buffer(void* h, int* width, int* height, int* stride);
void moavi_rdp_mouse(void* h, int flags, int x, int y);
void moavi_rdp_key(void* h, int down, int scancode);
void moavi_rdp_clipboard(void* h, const char* utf8);
void moavi_rdp_clipboard_on(void* h, int on);
void moavi_rdp_device(void* h, int token, int printer, const char* name, const char* path, int on);
void moavi_rdp_resize(void* h, int width, int height, int scale);
void moavi_rdp_disconnect(void* h);
void moavi_rdp_free(void* h);

static volatile int frames = 0, connected = 0, ended = 0, sized_w = 0, sized_h = 0;
static char end_message[256];

static void on_frame(void* u, int x, int y, int w, int h) { frames++; }
static void on_size(void* u, int w, int h) { sized_w = w; sized_h = h; printf("size %dx%d\n", w, h); }
static void on_clip(void* u, const char* t) { printf("remote clipboard: %s\n", t); }
static void on_state(void* u, int s, const char* m)
{
	printf("state %d %s\n", s, m ? m : "");
	if (s == 1)
		connected = 1;
	if (s == 2)
	{
		ended = 1;
		snprintf(end_message, sizeof(end_message), "%s", m ? m : "");
	}
}

static void* run(void* h)
{
	int rc = moavi_rdp_run(h);
	printf("run returned %d\n", rc);
	return NULL;
}

int main(int argc, char** argv)
{
	char** args = calloc((size_t)argc + 1, sizeof(char*));
	args[0] = "moavi-desk";
	for (int i = 1; i < argc; i++)
		args[i] = argv[i];
	void* h = moavi_rdp_new(argc, args, on_frame, on_size, on_clip, on_state, NULL);
	if (!h)
	{
		printf("FAIL: moavi_rdp_new\n");
		return 1;
	}
	moavi_rdp_clipboard_on(h, 1);
	moavi_rdp_device(h, 1, 0, "test", "/tmp", 1);   /* before the device channel: waits for it */

	pthread_t t;
	pthread_create(&t, NULL, run, h);
	for (int i = 0; i < 300 && !connected && !ended; i++)
		usleep(100000);
	if (!connected)
	{
		printf("FAIL: not connected (%s)\n", end_message);
		return 1;
	}
	for (int i = 0; i < 200 && frames < 3; i++)
		usleep(100000);
	int w = 0, hh = 0, stride = 0;
	unsigned char* buf = moavi_rdp_buffer(h, &w, &hh, &stride);
	long lit = 0;
	for (int y = 0; buf && y < hh; y += 4)
		for (int x = 0; x < w; x += 4)
			if (buf[y * stride + x * 4] | buf[y * stride + x * 4 + 1] | buf[y * stride + x * 4 + 2])
				lit++;
	printf("frames %d, buffer %dx%d stride %d, lit samples %ld\n", frames, w, hh, stride, lit);

	/* input, devices, clipboard, size */
	moavi_rdp_mouse(h, 0x0800, w / 2, hh / 2);
	moavi_rdp_key(h, 1, 0x1E);
	moavi_rdp_key(h, 0, 0x1E);
	moavi_rdp_clipboard(h, "MOAVI Desk 클립보드 시험");
	moavi_rdp_device(h, 2, 0, "tmp2", "/tmp", 1);
	sleep(2);
	moavi_rdp_device(h, 2, 0, "tmp2", "/tmp", 0);
	moavi_rdp_device(h, 1, 0, "test", "/tmp", 0);
	moavi_rdp_resize(h, 1280, 720, 100);
	sleep(3);
	int ok = frames >= 1 && lit > 0 && !ended;
	printf("after input: frames %d, still connected %d\n", frames, !ended);

	moavi_rdp_disconnect(h);
	pthread_join(t, NULL);
	moavi_rdp_free(h);
	printf("%s\n", ok ? "PASS" : "FAIL");
	return ok ? 0 : 1;
}
