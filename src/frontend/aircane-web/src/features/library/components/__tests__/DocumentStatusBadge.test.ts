import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import DocumentStatusBadge from '../DocumentStatusBadge.vue'

describe('DocumentStatusBadge', () => {
  it('renders the parsed state with the parsed badge art', () => {
    const wrapper = mount(DocumentStatusBadge, { props: { status: 'parsed' } })
    expect(wrapper.text()).toContain('Parsed')
    expect(wrapper.classes()).toContain('parsed')
    expect(wrapper.attributes('style')).toContain('document-status-parsed-badge.png')
    expect(wrapper.attributes('aria-label')).toBe('Document status: Parsed')
    // No spinner for parsed.
    expect(wrapper.find('svg.spinner').exists()).toBe(false)
  })

  it('renders the processing state with a spinner and the processing badge art', () => {
    const wrapper = mount(DocumentStatusBadge, { props: { status: 'processing' } })
    expect(wrapper.text()).toContain('Processing')
    expect(wrapper.classes()).toContain('processing')
    expect(wrapper.attributes('style')).toContain('document-status-processing-badge.png')
    expect(wrapper.find('svg.spinner').exists()).toBe(true)
  })

  it('renders the error state with CSS only (no badge image)', () => {
    const wrapper = mount(DocumentStatusBadge, { props: { status: 'error' } })
    expect(wrapper.text()).toContain('Error')
    expect(wrapper.classes()).toContain('error')
    // No PNG exists for error — it must not set a background image.
    expect(wrapper.attributes('style') ?? '').not.toContain('.png')
    expect(wrapper.find('svg.spinner').exists()).toBe(false)
  })

  it('renders the ocr-required state with CSS only (no badge image)', () => {
    const wrapper = mount(DocumentStatusBadge, { props: { status: 'ocr-required' } })
    expect(wrapper.text()).toContain('OCR Required')
    expect(wrapper.classes()).toContain('ocr-required')
    expect(wrapper.attributes('style') ?? '').not.toContain('.png')
    expect(wrapper.attributes('aria-label')).toBe('Document status: OCR Required')
  })
})
